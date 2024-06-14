using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ModbusIntegration.Modbus
{
    public class ModbusTCP_Client
    {
        public enum ByteOrder
        {
            ABCD, BADC, CDAB, DCBA
        }

        public ByteOrder Order = ByteOrder.CDAB;

        private SemaphoreSlim sendLock;

        private Random random;

        public int TransactionMeta { get; private set; }

        /// <summary>
        /// 服务器IP
        /// </summary>
        public string Target_IP { get; private set; }

        /// <summary>
        /// 服务器端口号
        /// </summary>
        public int Target_Port { get; private set; }

        /// <summary>
        /// 连接状态
        /// </summary>
        public bool IsConnect { get; private set; }

        /// <summary>
        /// 接收缓冲区
        /// </summary>
        private byte[] ReadBuffer { get; set; } = new byte[1024 * 1024];

        /// <summary>
        /// 断开事件
        /// </summary>
        public event Action<DateTime, Exception> DisconnectionEvent;

        /// <summary>
        /// 连接事件
        /// </summary>
        public event Action<DateTime, IPAddress> SuccessfuConnectEvent;

        /// <summary>
        /// 耗时，操作结果，读取、写入，起始地址，连续寄存器数，操作值
        /// </summary>
        public event Action<TimeSpan, bool, string, string, int, string> InteractionEvent;

        private int GC_Lock = 0;

        private System.Net.Sockets.TcpClient tcpClient { get; set; }

        /// <summary>
        /// 站号
        /// </summary>
        public ushort StationNumber { get; set; } = 1;

        public ModbusTCP_Client()
        {

        }

        /// <summary>
        /// 连接服务器
        /// </summary>
        /// <param name="targetip">服务器ip</param>
        /// <param name="targetport">服务器端口号</param>
        public ModbusTCP_Client(string targetip, int targetport) : this(targetip, targetport, ByteOrder.CDAB)
        {
        }

        /// <summary>
        /// 连接服务器
        /// </summary>
        /// <param name="targetip">服务器ip</param>
        /// <param name="targetport">服务器端口号</param>
        public ModbusTCP_Client(string targetip, int targetport, ByteOrder order)
        {
            Order = order;
            sendLock = new SemaphoreSlim(1, 1);
            AsyncNewTcp(targetip, targetport);
        }

        public void OnInteractionEvent(TimeSpan timeSpan, bool result, string readWrite, string startAddress, int registerCount, string operationValue)
        {
            if (GC_Lock > 100)
            {
                GC.Collect();
                GC_Lock = 0;
            }
            InteractionEvent?.Invoke(timeSpan, result, readWrite, startAddress, registerCount, operationValue);
            GC_Lock++;
        }

        public void AsyncNewTcp(string targetip, int targetport)
        {
            this.Target_IP = targetip;
            this.Target_Port = targetport;
            try
            {
                if (tcpClient != null)
                {
                    tcpClient.Close();
                    tcpClient.Dispose();
                    tcpClient = null;
                }
                tcpClient = new System.Net.Sockets.TcpClient();
                tcpClient.BeginConnect(IPAddress.Parse(Target_IP), Target_Port, new AsyncCallback(AsyncConnect), tcpClient);
            }
            catch (Exception ex)
            {
                DisconnectionEvent?.BeginInvoke(DateTime.Now, ex, null, null);
                throw new Exception(ex.Message + "\r" + ex.StackTrace);
            }
        }

        private void AsyncConnect(IAsyncResult async)
        {
            async.AsyncWaitHandle.WaitOne(3000);
            if (tcpClient.Client != null)
            {
                if (!tcpClient.Connected)
                {
                    IsConnect = false;
                    tcpClient.Close();
                    tcpClient = null;
                    AsyncNewTcp(Target_IP, Target_Port);
                }
                else
                {
                    try
                    {
                        IsConnect = true;
                        SuccessfuConnectEvent?.BeginInvoke(DateTime.Now, IPAddress.Parse(Target_IP), null, null);
                        tcpClient.EndConnect(async);
                    }
                    catch (Exception ex)
                    {
                        DisconnectionEvent?.BeginInvoke(DateTime.Now, ex, null, null);
                        IsConnect = false;
                        tcpClient.Close();
                        tcpClient = null;
                        AsyncNewTcp(Target_IP, Target_Port);
                    }
                }
            }
        }

        public static byte[] ReverseBytes(byte[] input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            byte[] result = new byte[input.Length];
            for (int i = 0; i < input.Length; i++)
            {
                result[i] = input[input.Length - 1 - i];
            }
            return result;
        }

        public static byte[][] ReverseBytes(byte[][] input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            byte[][] result = new byte[input.Length][];
            for (int i = 0; i < input.Length; i++)
            {
                result[i] = ReverseBytes(input[i]);
            }
            return result;
        }

        private async Task<Package<T>> AsyncSendReceive<T>(Package<T> syncSend)
        {
            try
            {
                await sendLock.WaitAsync();
                if (IsConnect)
                {
                    DateTime startTime = DateTime.Now;
                    syncSend.ReceiveBuff = new byte[syncSend.SendBuff.Length][];
                    var DataBuff = new byte[syncSend.SendBuff.Length][];
                    bool[] success = new bool[syncSend.SendBuff.Length];
                    for (int i = 0; i < syncSend.SendBuff.Length; i++)
                    {
                        await tcpClient.GetStream().WriteAsync(syncSend.SendBuff[i], 0, syncSend.SendBuff[i].Length);
                        int bytesRead = await tcpClient.GetStream().ReadAsync(ReadBuffer, 0, ReadBuffer.Length);
                        syncSend.ReceiveBuff[i] = new byte[bytesRead];
                        Array.Copy(ReadBuffer, 0, syncSend.ReceiveBuff[i], 0, bytesRead);
                        if (bytesRead > 0)
                        {
                            if (ReadBuffer[0] != syncSend.SendBuff[i][0] ||
                                ReadBuffer[1] != syncSend.SendBuff[i][1] ||
                                ReadBuffer[7] != syncSend.SendBuff[i][7])
                            {
                                success[i] = false;
                            }
                            else
                            {
                                syncSend.FunctionCode = ReadBuffer[7];
                                DataBuff[i] = new byte[syncSend.ReceiveBuff[i][8]];
                                Array.Copy(syncSend.ReceiveBuff[i], 9, DataBuff[i], 0, DataBuff[i].Length);
                                //DataBuff[i] = SwapByteOrder(DataBuff[i]);
                                success[i] = true;
                            }
                        }
                    }
                    if (DataBuff == null) throw new ArgumentNullException(nameof(DataBuff));
                    int totalLength = 0;
                    foreach (var subArray in DataBuff)
                    {
                        totalLength += subArray.Length;
                    }
                    syncSend.DataBuff = new byte[totalLength];
                    int offset = 0;
                    foreach (var subArray in DataBuff)
                    {
                        Buffer.BlockCopy(subArray, 0, syncSend.DataBuff, offset, subArray.Length);
                        offset += subArray.Length;
                    }
                    Array.Reverse(syncSend.DataBuff);
                    syncSend.IsSuccess = success.All(x => x);
                    DateTime endTime = DateTime.Now;
                    syncSend.ElapsedTime = endTime - startTime;
                }
                else
                {
                    DisconnectionEvent?.BeginInvoke(DateTime.Now, new Exception("未连接到服务器"), null, null);
                }
            }
            catch (Exception ex)
            {
                DisconnectionEvent?.BeginInvoke(DateTime.Now, ex, null, null);
                tcpClient.Close();
                IsConnect = false;
                tcpClient = null;
                AsyncNewTcp(Target_IP, Target_Port);
            }
            finally
            {
                sendLock.Release();
            }
            return syncSend;
        }

        private async Task<Package<T>> AsyncReceive<T>(Package<T> syncSend)
        {
            try
            {
                await sendLock.WaitAsync();
                if (IsConnect)
                {
                    DateTime startTime = DateTime.Now;
                    syncSend.ReceiveBuff = new byte[syncSend.SendBuff.Length][];
                    var DataBuff = new byte[syncSend.SendBuff.Length][];
                    bool[] success = new bool[syncSend.SendBuff.Length];
                    for (int i = 0; i < syncSend.SendBuff.Length; i++)
                    {
                        await tcpClient.GetStream().WriteAsync(syncSend.SendBuff[i], 0, syncSend.SendBuff[i].Length);
                        int bytesRead = await tcpClient.GetStream().ReadAsync(ReadBuffer, 0, ReadBuffer.Length);
                        syncSend.ReceiveBuff[i] = new byte[bytesRead];
                        Array.Copy(ReadBuffer, 0, syncSend.ReceiveBuff[i], 0, bytesRead);
                        if (bytesRead > 0)
                        {
                            if (ReadBuffer[0] != syncSend.SendBuff[i][0] ||
                                ReadBuffer[1] != syncSend.SendBuff[i][1] ||
                                ReadBuffer[7] != syncSend.SendBuff[i][7])
                            {
                                success[i] = false;
                            }
                            else
                            {
                                syncSend.FunctionCode = ReadBuffer[7];
                                DataBuff[i] = new byte[syncSend.ReceiveBuff[i].Length];
                                Array.Copy(syncSend.ReceiveBuff[i], 0, DataBuff[i], 0, DataBuff[i].Length);
                                //DataBuff[i] = SwapByteOrder(DataBuff[i]);
                                success[i] = true;
                            }
                        }
                    }
                    if (DataBuff == null) throw new ArgumentNullException(nameof(DataBuff));
                    int totalLength = 0;
                    foreach (var subArray in DataBuff)
                    {
                        totalLength += subArray.Length;
                    }
                    syncSend.DataBuff = new byte[totalLength];
                    int offset = 0;
                    foreach (var subArray in DataBuff)
                    {
                        Buffer.BlockCopy(subArray, 0, syncSend.DataBuff, offset, subArray.Length);
                        offset += subArray.Length;
                    }
                    Array.Reverse(syncSend.DataBuff);
                    syncSend.IsSuccess = success.All(x => x);
                    DateTime endTime = DateTime.Now;
                    syncSend.ElapsedTime = endTime - startTime;
                }
                else
                {
                    DisconnectionEvent?.BeginInvoke(DateTime.Now, new Exception("未连接到服务器"), null, null);
                }
            }
            catch (Exception ex)
            {
                DisconnectionEvent?.BeginInvoke(DateTime.Now, ex, null, null);
                tcpClient.Close();
                IsConnect = false;
                tcpClient = null;
                AsyncNewTcp(Target_IP, Target_Port);
            }
            finally
            {
                sendLock.Release();
            }
            return syncSend;
        }

        private Package<T> AsyncSendReceive<T>(ref Package<T> syncSend)
        {
            try
            {
                sendLock.Wait();
                if (IsConnect)
                {
                    DateTime startTime = DateTime.Now;
                    syncSend.ReceiveBuff = new byte[syncSend.SendBuff.Length][];
                    var DataBuff = new byte[syncSend.SendBuff.Length][];
                    bool[] success = new bool[syncSend.SendBuff.Length];
                    for (int i = 0; i < syncSend.SendBuff.Length; i++)
                    {
                        tcpClient.GetStream().Write(syncSend.SendBuff[i], 0, syncSend.SendBuff[i].Length);
                        int bytesRead = tcpClient.GetStream().Read(ReadBuffer, 0, ReadBuffer.Length);
                        syncSend.ReceiveBuff[i] = new byte[bytesRead];
                        Array.Copy(ReadBuffer, 0, syncSend.ReceiveBuff[i], 0, bytesRead);
                        if (bytesRead > 0)
                        {
                            if (ReadBuffer[0] != syncSend.SendBuff[i][0] ||
                                ReadBuffer[1] != syncSend.SendBuff[i][1] ||
                                ReadBuffer[7] != syncSend.SendBuff[i][7])
                            {
                                success[i] = false;
                            }
                            else
                            {
                                syncSend.FunctionCode = ReadBuffer[7];
                                DataBuff[i] = new byte[syncSend.ReceiveBuff[i][8]];
                                Array.Copy(syncSend.ReceiveBuff[i], 9, DataBuff[i], 0, DataBuff[i].Length);
                                success[i] = true;
                            }
                        }
                    }
                    if (DataBuff == null)
                        throw new ArgumentNullException(nameof(DataBuff));
                    int totalLength = 0;
                    foreach (var subArray in DataBuff)
                    {
                        totalLength += subArray.Length;
                    }
                    syncSend.DataBuff = new byte[totalLength];
                    int offset = 0;
                    foreach (var subArray in DataBuff)
                    {
                        Buffer.BlockCopy(subArray, 0, syncSend.DataBuff, offset, subArray.Length);
                        offset += subArray.Length;
                    }
                    Array.Reverse(syncSend.DataBuff);
                    syncSend.IsSuccess = success.All(x => x);
                    DateTime endTime = DateTime.Now;
                    syncSend.ElapsedTime = endTime - startTime;
                }
                else
                {
                    DisconnectionEvent?.BeginInvoke(DateTime.Now, new Exception("未连接到服务器"), null, null);
                }
            }
            catch (Exception ex)
            {
                DisconnectionEvent?.BeginInvoke(DateTime.Now, ex, null, null);
                tcpClient.Close();
                IsConnect = false;
                tcpClient = null;
                AsyncNewTcp(Target_IP, Target_Port);
            }
            finally
            {
                sendLock.Release();
            }
            return syncSend;
        }

        private Package<T> AsyncReceive<T>(ref Package<T> syncSend)
        {
            try
            {
                sendLock.Wait();
                if (IsConnect)
                {
                    DateTime startTime = DateTime.Now;
                    syncSend.ReceiveBuff = new byte[syncSend.SendBuff.Length][];
                    var DataBuff = new byte[syncSend.SendBuff.Length][];
                    bool[] success = new bool[syncSend.SendBuff.Length];
                    for (int i = 0; i < syncSend.SendBuff.Length; i++)
                    {
                        tcpClient.GetStream().Write(syncSend.SendBuff[i], 0, syncSend.SendBuff[i].Length);
                        int bytesRead = tcpClient.GetStream().Read(ReadBuffer, 0, ReadBuffer.Length);
                        syncSend.ReceiveBuff[i] = new byte[bytesRead];
                        Array.Copy(ReadBuffer, 0, syncSend.ReceiveBuff[i], 0, bytesRead);
                        if (bytesRead > 0)
                        {
                            if (ReadBuffer[0] != syncSend.SendBuff[i][0] ||
                                ReadBuffer[1] != syncSend.SendBuff[i][1] ||
                                ReadBuffer[7] != syncSend.SendBuff[i][7])
                            {
                                success[i] = false;
                            }
                            else
                            {
                                syncSend.FunctionCode = ReadBuffer[7];
                                DataBuff[i] = new byte[syncSend.ReceiveBuff[i].Length];
                                Array.Copy(syncSend.ReceiveBuff[i], 0, DataBuff[i], 0, DataBuff[i].Length);
                                success[i] = true;
                            }
                        }
                    }
                    if (DataBuff == null) throw new ArgumentNullException(nameof(DataBuff));
                    int totalLength = 0;
                    foreach (var subArray in DataBuff)
                    {
                        totalLength += subArray.Length;
                    }
                    syncSend.DataBuff = new byte[totalLength];
                    int offset = 0;
                    foreach (var subArray in DataBuff)
                    {
                        Buffer.BlockCopy(subArray, 0, syncSend.DataBuff, offset, subArray.Length);
                        offset += subArray.Length;
                    }
                    Array.Reverse(syncSend.DataBuff);
                    syncSend.IsSuccess = success.All(x => x);
                    DateTime endTime = DateTime.Now;
                    syncSend.ElapsedTime = endTime - startTime;
                }
                else
                {
                    DisconnectionEvent?.BeginInvoke(DateTime.Now, new Exception("未连接到服务器"), null, null);
                }
            }
            catch (Exception ex)
            {
                DisconnectionEvent?.BeginInvoke(DateTime.Now, ex, null, null);
                tcpClient.Close();
                IsConnect = false;
                tcpClient = null;
                AsyncNewTcp(Target_IP, Target_Port);
            }
            finally
            {
                sendLock.Release();
            }
            return syncSend;
        }


        private byte[] GetReadMessage(byte function, string address, int length)
        {
            if (length <= 125)
            {
                byte[] msg = new byte[12];
                if (random == null)
                    random = new Random();
                TransactionMeta = random.Next(0, 65535);
                msg[0] = (byte)(TransactionMeta >> 8);
                msg[1] = (byte)(TransactionMeta & 0xFF);
                msg[5] = 0x06;
                msg[6] = (byte)StationNumber;
                msg[7] = function;
                ushort addr = ushort.Parse(address);
                msg[8] = (byte)(addr >> 8);
                msg[9] = (byte)(addr & 0xFF);
                msg[10] = (byte)(length >> 8);
                msg[11] = (byte)(length & 0xFF);
                return msg;
            }
            else
            {
                throw new ArgumentException($"读取起始地址：{address},单次读取ModbusTCP的寄存器个数超出125");
            }
        }

        private byte[] GetWriteMessage(byte function, string address, byte[] value)
        {
            byte[] msg = new byte[10 + value.Length];
            if (random == null)
                random = new Random();
            TransactionMeta = random.Next(0, 65535);
            msg[0] = (byte)(TransactionMeta >> 8);
            msg[1] = (byte)(TransactionMeta & 0xFF);
            msg[5] = (byte)((byte)msg.Length - 6);
            msg[6] = (byte)StationNumber;
            msg[7] = function;
            ushort addr = ushort.Parse(address);
            msg[8] = (byte)(addr >> 8);
            msg[9] = (byte)(addr & 0xFF);
            Array.Copy(value, 0, msg, 10, value.Length);
            return msg;
        }

        private byte[][] GetReadMessages(byte function, string address, int length)
        {
            int numMessages = (length + 124) / 125;
            byte[][] result = new byte[numMessages][];
            int remainingLength = length;
            int startAddress = int.Parse(address);
            if (random == null)
                random = new Random();
            for (int i = 0; i < numMessages; i++)
            {
                int currentLength = Math.Min(remainingLength, 125);
                byte[] msg = new byte[12];
                TransactionMeta = random.Next(0, 65535);
                msg[0] = (byte)(TransactionMeta >> 8);
                msg[1] = (byte)(TransactionMeta & 0xFF);
                msg[5] = 0x06;
                msg[6] = (byte)StationNumber;
                msg[7] = function;
                ushort addr = (ushort)(startAddress + (length - remainingLength));
                msg[8] = (byte)(addr >> 8);
                msg[9] = (byte)(addr & 0xFF);
                msg[10] = (byte)(currentLength >> 8);
                msg[11] = (byte)(currentLength & 0xFF);
                result[i] = new byte[12];
                for (int j = 0; j < 12; j++)
                {
                    result[i][j] = msg[j];
                }
                remainingLength -= currentLength;
            }
            return result;
        }

        public static byte[] ConvertByteOrderBatch(byte[] bytes, int wordLength, ByteOrder byteOrder)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (wordLength <= 0 || bytes.Length % wordLength != 0)
                throw new ArgumentException("字节数组长度必须为字长的整数倍。");

            byte[] result = new byte[bytes.Length];
            int groupCount = bytes.Length / wordLength;

            for (int group = 0; group < groupCount; group++)
            {
                int offset = group * wordLength;

                switch (byteOrder)
                {
                    case ByteOrder.ABCD:
                        // ABCD表示字节顺序不变，直接拷贝
                        Array.Copy(bytes, offset, result, offset, wordLength);
                        break;

                    case ByteOrder.BADC:
                        // BADC字节顺序反转：适用于 4 字节 和 8 字节数据
                        if (wordLength == 4)
                        {
                            result[offset] = bytes[offset + 1];
                            result[offset + 1] = bytes[offset];
                            result[offset + 2] = bytes[offset + 3];
                            result[offset + 3] = bytes[offset + 2];
                        }
                        else if (wordLength == 8)
                        {
                            // 对 8 字节数据（如 double 类型），按照 BADC 顺序反转
                            result[offset] = bytes[offset + 1];
                            result[offset + 1] = bytes[offset];
                            result[offset + 2] = bytes[offset + 3];
                            result[offset + 3] = bytes[offset + 2];
                            result[offset + 4] = bytes[offset + 5];
                            result[offset + 5] = bytes[offset + 4];
                            result[offset + 6] = bytes[offset + 7];
                            result[offset + 7] = bytes[offset + 6];
                        }
                        else
                        {
                            Array.Copy(bytes, offset, result, offset, wordLength);
                            Array.Reverse(result);
                        }
                        break;

                    case ByteOrder.CDAB:
                        // CDAB字节顺序反转：适用于 4 字节 和 8 字节数据
                        if (wordLength == 4)
                        {
                            result[offset] = bytes[offset + 2];
                            result[offset + 1] = bytes[offset + 3];
                            result[offset + 2] = bytes[offset];
                            result[offset + 3] = bytes[offset + 1];
                        }
                        else if (wordLength == 8)
                        {
                            // 对 8 字节数据（如 double 类型），按照 CDAB 顺序反转
                            result[offset] = bytes[offset + 4];
                            result[offset + 1] = bytes[offset + 5];
                            result[offset + 2] = bytes[offset + 6];
                            result[offset + 3] = bytes[offset + 7];
                            result[offset + 4] = bytes[offset];
                            result[offset + 5] = bytes[offset + 1];
                            result[offset + 6] = bytes[offset + 2];
                            result[offset + 7] = bytes[offset + 3];
                        }
                        else
                        {
                            Array.Copy(bytes, offset, result, offset, wordLength);
                            Array.Reverse(result);
                        }
                        break;

                    case ByteOrder.DCBA:
                        // 任意字节长度的字节顺序反转
                        for (int i = 0; i < wordLength; i++)
                        {
                            result[offset + i] = bytes[offset + wordLength - 1 - i];
                        }
                        break;

                    default:
                        throw new ArgumentException("不支持的字节顺序。");
                }
            }

            return result;
        }

        private static bool[] BytesToBitsReversed(byte[] bytes, int maxBits)
        {
            if (bytes == null)
                throw new ArgumentNullException("byte[] bytes为null");
            bool[] bits = new bool[maxBits];
            int bitsCount = 0;
            for (int i = 0; i < bytes.Length && bitsCount < maxBits; i++)
            {
                byte b = bytes[i];
                for (int j = 0; j < 8 && bitsCount < maxBits; j++)
                {
                    bits[bitsCount] = (b & (1 << j)) != 0;
                    bitsCount++;
                }
            }
            return bits;
        }

        public static byte[] GetBytesBigEndian(int value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }
            return bytes;
        }

        public static byte[] GetBytesBigEndian(float value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }
            return bytes;
        }

        public static byte[] GetBytesBigEndian(double value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }
            return bytes;
        }

        #region Synchronization

        public Package<bool> ReadBool(string address, byte function = 0x01)
        {
            Package<bool> accept = new Package<bool>();
            if (IsConnect)
            {
                if (function != 0x01 && function != 0x02)
                    throw new ArgumentException("byte function参数指定功能码错误");
                accept.SendBuff = GetReadMessages(function, address, 1);
                AsyncSendReceive(ref accept);
                if (accept.IsSuccess)
                {
                    bool decimalValue = BitConverter.ToBoolean(accept.DataBuff, 0);
                    accept.Value = decimalValue;
                }
                OnInteractionEvent(accept.ElapsedTime, accept.IsSuccess, "ReadBool", address, 1, accept.Value.ToString());
            }
            return accept;
        }

        public Package<bool[]> ReadBool(string address, int length, byte function = 0x01)
        {
            Package<bool[]> accept = new Package<bool[]>();
            string result = "Null";
            if (IsConnect)
            {
                if (function != 0x01 && function != 0x02)
                    throw new ArgumentException("byte function参数指定功能码错误");
                accept.SendBuff = GetReadMessages(function, address, 1 * length);
                AsyncSendReceive(ref accept);
                if (accept.IsSuccess)
                {
                    bool[] bools = new bool[length];
                    accept.DataBuff = ReverseBytes(accept.DataBuff);
                    bools = BytesToBitsReversed(accept.DataBuff, length);
                    accept.Value = bools;
                    result = string.Join(", ", accept.Value.Select((value, index) => $"{index}={value}"));
                }
                OnInteractionEvent(accept.ElapsedTime, accept.IsSuccess, "ReadBool[]", address, length, result);

            }
            return accept;
        }

        public Package<byte[]> ReadBytes(string address, int registerLength, byte function = 0x03)
        {
            Package<byte[]> accept = new Package<byte[]>();
            string result = "Null";
            if (IsConnect)
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("byte function参数指定功能码错误");
                accept.SendBuff = GetReadMessages(function, address, registerLength);
                AsyncSendReceive(ref accept);
                if (accept.IsSuccess)
                {
                    accept.DataBuff = ReverseBytes(accept.DataBuff);
                    accept.Value = accept.DataBuff;
                    result = BitConverter.ToString(accept.Value);
                }
                OnInteractionEvent(accept.ElapsedTime, accept.IsSuccess, "ReadBytes[]", address, registerLength, result);
            }
            return accept;
        }

        public Package<byte[]> ReadBits(string address, int registerLength, byte function = 0x03)
        {
            Package<byte[]> accept = new Package<byte[]>();
            string result = "Null";
            if (IsConnect)
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("byte function参数指定功能码错误");
                accept.SendBuff = GetReadMessages(function, address, registerLength * 1);
                AsyncSendReceive(ref accept);
                if (accept.IsSuccess)
                {
                    int totalBits = registerLength * 16;
                    byte[] bitStates = new byte[totalBits];
                    for (int i = 0; i < registerLength; i++)
                    {
                        ushort currentRegister = BitConverter.ToUInt16(accept.DataBuff, i * 2);
                        for (int j = 0; j < 16; j++)
                        {
                            int bitIndex = i * 16 + j;
                            bitStates[bitIndex] = (byte)((currentRegister & (1 << (15 - j))) != 0 ? 1 : 0);// 获取该bit状态：如果是1，则为true（高电平）；否则为0（低电平）
                            //bitStates[bitIndex] = (byte)((currentRegister & (1 << j)) != 0 ? 1 : 0);
                        }
                    }
                    accept.Value = ReverseBytes(bitStates);
                    result = string.Join(", ", bitStates.Select((value, index) => $"{index}={value}"));
                }
                OnInteractionEvent(accept.ElapsedTime, accept.IsSuccess, "ReadBits[]", address, registerLength, result);

            }
            return accept;
        }


        public Package<short> ReadIn16(string address, byte function = 0x03)
        {
            Package<short> accept = new Package<short>();
            if (IsConnect)
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("byte function参数指定功能码错误");
                accept.SendBuff = GetReadMessages(function, address, 1);
                AsyncSendReceive(ref accept);
                if (accept.IsSuccess)
                {
                    accept.DataBuff = ConvertByteOrderBatch(accept.DataBuff, 2, ByteOrder.ABCD);
                    short decimalValue = BitConverter.ToInt16(accept.DataBuff, 0);
                    accept.Value = decimalValue;
                }
                OnInteractionEvent(accept.ElapsedTime, accept.IsSuccess, "ReadIn16", address, 1, accept.Value.ToString());

            }
            return accept;
        }

        public Package<short[]> ReadIn16(string address, int length, byte function = 0x03)
        {
            Package<short[]> accept = new Package<short[]>();
            string result = "Null";
            if (IsConnect)
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("byte function参数指定功能码错误");
                accept.SendBuff = GetReadMessages(function, address, length * 1);
                AsyncSendReceive(ref accept);
                if (accept.IsSuccess)
                {
                    accept.DataBuff = ConvertByteOrderBatch(accept.DataBuff, 2, ByteOrder.ABCD);
                    short[] shorts = new short[accept.DataBuff.Length / 2];
                    for (int i = 0; i < shorts.Length; i++)
                        shorts[shorts.Length - 1 - i] = BitConverter.ToInt16(accept.DataBuff, i * 2);
                    accept.Value = shorts;
                    result = string.Join(", ", accept.Value.Select((value, index) => $"{index}={value}"));
                }
                OnInteractionEvent(accept.ElapsedTime, accept.IsSuccess, "ReadIn16[]", address, length, result);

            }
            return accept;
        }

        public Package<int> ReadIn32(string address, byte function = 0x03)
        {
            Package<int> accept = new Package<int>();
            if (IsConnect)
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("byte function参数指定功能码错误");
                accept.SendBuff = GetReadMessages(function, address, 2);
                AsyncSendReceive(ref accept);
                if (accept.IsSuccess)
                {
                    accept.DataBuff = ConvertByteOrderBatch(accept.DataBuff, 4, Order);
                    int decimalValue = BitConverter.ToInt32(accept.DataBuff, 0);
                    accept.Value = decimalValue;
                }
                OnInteractionEvent(accept.ElapsedTime, accept.IsSuccess, "ReadIn32", address, 2, accept.Value.ToString());
            }
            return accept;
        }

        public Package<int[]> ReadIn32(string address, int length, byte function = 0x03)
        {
            Package<int[]> accept = new Package<int[]>();
            string result = "Null";
            if (IsConnect)
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("byte function参数指定功能码错误");
                accept.SendBuff = GetReadMessages(function, address, length * 2);
                AsyncSendReceive(ref accept);
                if (accept.IsSuccess)
                {
                    accept.DataBuff = ConvertByteOrderBatch(accept.DataBuff, 4, Order);
                    int[] ints = new int[accept.DataBuff.Length / 4];
                    for (int i = 0; i < ints.Length; i++)
                        ints[ints.Length - 1 - i] = BitConverter.ToInt32(accept.DataBuff, i * 4);
                    accept.Value = ints;
                    result = string.Join(", ", accept.Value.Select((value, index) => $"{index}= {value}"));
                }
                OnInteractionEvent(accept.ElapsedTime, accept.IsSuccess, "ReadIn32[]", address, length, result);

            }
            return accept;
        }

        public Package<float> ReadInFloat(string address, byte function = 0x03)
        {
            Package<float> accept = new Package<float>();
            if (IsConnect)
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("byte function参数指定功能码错误");
                accept.SendBuff = GetReadMessages(function, address, 2);
                AsyncSendReceive(ref accept);
                if (accept.IsSuccess)
                {
                    accept.DataBuff = ConvertByteOrderBatch(accept.DataBuff, 4, Order);
                    float decimalValue = BitConverter.ToSingle(accept.DataBuff, 0);
                    accept.Value = decimalValue;
                }
                OnInteractionEvent(accept.ElapsedTime, accept.IsSuccess, "ReadInFloat", address, 2, accept.Value.ToString());

            }
            return accept;
        }

        public Package<float[]> ReadInFloat(string address, int length, byte function = 0x03)
        {
            Package<float[]> accept = new Package<float[]>();
            string result = "Null";
            if (IsConnect)
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("byte function参数指定功能码错误");
                accept.SendBuff = GetReadMessages(function, address, length * 2);
                AsyncSendReceive(ref accept);
                if (accept.IsSuccess)
                {
                    accept.DataBuff = ConvertByteOrderBatch(accept.DataBuff, 4, Order);
                    float[] floats = new float[accept.DataBuff.Length / 4];
                    for (int i = 0; i < floats.Length; i++)
                        floats[floats.Length - 1 - i] = BitConverter.ToSingle(accept.DataBuff, i * 4);
                    accept.Value = floats;
                    result = string.Join(", ", accept.Value.Select((value, index) => $"{index}={value}"));
                }
                OnInteractionEvent(accept.ElapsedTime, accept.IsSuccess, "ReadInFloat[]", address, length, result);

            }
            return accept;
        }

        public Package<double> ReadInDouble(string address, byte function = 0x03)
        {
            Package<double> accept = new Package<double>();
            if (IsConnect)
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("byte function参数指定功能码错误");
                accept.SendBuff = GetReadMessages(function, address, 4);
                AsyncSendReceive(ref accept);
                if (accept.IsSuccess)
                {
                    accept.DataBuff = ConvertByteOrderBatch(accept.DataBuff, 8, Order);
                    double decimalValue = BitConverter.ToDouble(accept.DataBuff, 0);
                    accept.Value = decimalValue;
                }
                OnInteractionEvent(accept.ElapsedTime, accept.IsSuccess, "ReadInFloat", address, 4, accept.Value.ToString());

            }
            return accept;
        }

        public Package<double[]> ReadInDouble(string address, int length, byte function = 0x03)
        {
            Package<double[]> accept = new Package<double[]>();
            string result = "Null";
            if (IsConnect)
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("byte function参数指定功能码错误");
                accept.SendBuff = GetReadMessages(function, address, length * 8);
                AsyncSendReceive(ref accept);
                if (accept.IsSuccess)
                {
                    accept.DataBuff = ConvertByteOrderBatch(accept.DataBuff, 8, Order);
                    double[] floats = new double[accept.DataBuff.Length];
                    for (int i = 0; i < floats.Length; i++)
                        floats[floats.Length - 1 - i] = BitConverter.ToDouble(accept.DataBuff, i * 8);
                    accept.Value = floats;
                    result = string.Join(", ", accept.Value.Select((value, index) => $"{index}={value}"));
                }
                OnInteractionEvent(accept.ElapsedTime, accept.IsSuccess, "ReadInFloat[]", address, length, result);

            }
            return accept;
        }

        public Package<string> ReadInString(string address, int length, byte function = 0x03)
        {
            Package<string> accept = new Package<string>();
            if (IsConnect)
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("byte function参数指定功能码错误");
                accept.SendBuff = GetReadMessages(function, address, length * 2);
                AsyncSendReceive(ref accept);
                if (accept.IsSuccess)
                {
                    var data = accept.DataBuff.ToArray();
                    List<char> chars = new List<char>();
                    for (int i = 0; i < data.Length; i++)
                    {
                        if (data[i] != 0)
                            chars.Add((char)data[i]);
                    }
                    chars.Reverse();
                    for (int i = 0; i < chars.Count - 1; i += 2)
                    {
                        char temp = chars[i];
                        chars[i] = chars[i + 1];
                        chars[i + 1] = temp;
                    }
                    accept.Value = new string(chars.ToArray());
                }
                OnInteractionEvent(accept.ElapsedTime, accept.IsSuccess, "ReadInString", address, length * 2, accept.Value?.ToString());
            }
            return accept;
        }


        public Package<bool> WriteBool(string address, bool value)
        {
            Package<bool> accept = new Package<bool>();
            if (IsConnect)
            {
                accept.SendBuff = new byte[1][];
                accept.SendBuff[0] = GetWriteMessage(0x05, address, new byte[] { (byte)(value ? 0xff : 0), 0 });
                AsyncReceive(ref accept);
                accept.DataBuff = new byte[2] { accept.ReceiveBuff[0][accept.ReceiveBuff[0].Length - 2], accept.ReceiveBuff[0][accept.ReceiveBuff[0].Length - 1] };
                OnInteractionEvent(accept.ElapsedTime, accept.IsSuccess, "WriteBool", address, 1, value.ToString());
            }
            return accept;
        }

        public Package<bool[]> WriteBool(string address, bool[] value)
        {
            Package<bool[]> accept = new Package<bool[]>();
            string result = "Null";
            if (IsConnect)
            {
                int length = value.Length;
                int byteLength = (length + 7) / 8;
                byte[] result_byte = new byte[3 + byteLength];
                result_byte[0] = (byte)(length >> 8);
                result_byte[1] = (byte)(length & 0xFF);
                result_byte[2] = (byte)(byteLength);
                for (int i = 0; i < byteLength; i++)
                {
                    for (byte j = 0; j < 8; j++)
                    {
                        var a = i * 8 + j;
                        if (a < value.Length)
                        {
                            if (value[a])
                                result_byte[i + 3] |= (byte)(1 << j);
                        }
                        else
                            break;
                    }
                }
                accept.SendBuff = new byte[1][];
                accept.SendBuff[0] = GetWriteMessage(0x0f, address, result_byte);
                AsyncReceive(ref accept);
                if (accept.IsSuccess)
                {
                    accept.DataBuff = new byte[2] { accept.ReceiveBuff[0][accept.ReceiveBuff[0].Length - 2], accept.ReceiveBuff[0][accept.ReceiveBuff[0].Length - 1] };
                    result = string.Join(", ", value.Select((value_list, index) => $"{index}={value_list}"));
                }
                OnInteractionEvent(accept.ElapsedTime, accept.IsSuccess, "WriteBool[]", address, 1, result);

            }
            return accept;
        }

        public async Task<Package<short>> WriteIn16(string address, short value)
        {
            Package<short> accept = new Package<short>();
            if (IsConnect)
            {
                var send_ = ConvertByteOrderBatch(BitConverter.GetBytes(value), 2, Order);
                accept.SendBuff = new byte[1][];
                accept.SendBuff[0] = GetWriteMessage(0x06, address, send_);
                await AsyncReceive(accept);
                accept.DataBuff = new byte[2] { accept.ReceiveBuff[0][accept.ReceiveBuff[0].Length - 2], accept.ReceiveBuff[0][accept.ReceiveBuff[0].Length - 1] };
                OnInteractionEvent(accept.ElapsedTime, accept.IsSuccess, "WriteIn16", address, 1, value.ToString());
            }
            return accept;
        }

        public Package<int> WriteIn32(string address, int value)
        {
            Package<int> accept = new Package<int>();
            if (IsConnect)
            {
                var send_ = ConvertByteOrderBatch(GetBytesBigEndian(value), 4, Order);
                byte[] bytes = new byte[3 + send_.Length];
                bytes[0] = 0x00;
                bytes[1] = (byte)(send_.Length / 2);
                bytes[2] = (byte)send_.Length;
                Array.Copy(send_, 0, bytes, 3, send_.Length);
                accept.SendBuff = new byte[1][];
                accept.SendBuff[0] = GetWriteMessage(0x10, address, bytes);
                AsyncReceive(ref accept);
                accept.DataBuff = new byte[5];
                Array.Copy(accept.ReceiveBuff[0], accept.ReceiveBuff[0].Length - 5, accept.DataBuff, 0, accept.DataBuff.Length);
                OnInteractionEvent(accept.ElapsedTime, accept.IsSuccess, "WriteIn32", address, 1, value.ToString());
            }
            return accept;
        }

        public Package<float> WriteFloat(string address, float value)
        {
            Package<float> accept = new Package<float>();
            if (IsConnect)
            {
                var send_ = ConvertByteOrderBatch(GetBytesBigEndian(value), 4, Order);
                byte[] bytes = new byte[3 + send_.Length];
                bytes[0] = 0x00;
                bytes[1] = (byte)(send_.Length / 2);
                bytes[2] = (byte)send_.Length;
                Array.Copy(send_, 0, bytes, 3, send_.Length);
                accept.SendBuff = new byte[1][];
                accept.SendBuff[0] = GetWriteMessage(0x10, address, bytes);
                AsyncReceive(ref accept);
                accept.DataBuff = new byte[5];
                Array.Copy(accept.ReceiveBuff[0], accept.ReceiveBuff[0].Length - 5, accept.DataBuff, 0, accept.DataBuff.Length);
                OnInteractionEvent(accept.ElapsedTime, accept.IsSuccess, "WriteFloat", address, 1, value.ToString());
            }
            return accept;
        }

        public Package<double> WriteDouble(string address, double value)
        {
            Package<double> accept = new Package<double>();
            if (IsConnect)
            {
                var t1 = BitConverter.GetBytes(value);
                var send_ = ConvertByteOrderBatch(BitConverter.GetBytes(value), 8, Order);
                byte[] temp = new byte[4];
                Array.Copy(send_, 0, temp, 0, 4);
                Array.Copy(send_, 4, send_, 0, 4);
                Array.Copy(temp, 0, send_, 4, 4);
                byte[] bytes = new byte[3 + send_.Length];
                bytes[0] = 0x00;
                bytes[1] = (byte)(send_.Length / 2);
                bytes[2] = (byte)send_.Length;
                Array.Copy(send_, 0, bytes, 3, send_.Length);
                accept.SendBuff = new byte[1][];
                accept.SendBuff[0] = GetWriteMessage(0x10, address, bytes);
                AsyncReceive(ref accept);
                accept.DataBuff = new byte[5];
                Array.Copy(accept.ReceiveBuff[0], accept.ReceiveBuff[0].Length - 5, accept.DataBuff, 0, accept.DataBuff.Length);
                OnInteractionEvent(accept.ElapsedTime, accept.IsSuccess, "WriteDouble", address, 1, value.ToString());
            }
            return accept;
        }

        public Package<string> WriteString(string address, string value)
        {
            Package<string> accept = new Package<string>();
            if (IsConnect)
            {
                var send_ = Encoding.ASCII.GetBytes(value);
                byte[] bytes = new byte[3 + send_.Length];
                bytes[0] = 0x00;
                bytes[1] = (byte)(send_.Length / 2);
                bytes[2] = (byte)send_.Length;
                Array.Copy(send_, 0, bytes, 3, send_.Length);
                accept.SendBuff = new byte[1][];
                accept.SendBuff[0] = GetWriteMessage(0x10, address, bytes);
                AsyncReceive(ref accept);
                accept.DataBuff = new byte[5];
                Array.Copy(accept.ReceiveBuff[0], accept.ReceiveBuff[0].Length - 5, accept.DataBuff, 0, accept.DataBuff.Length);
                OnInteractionEvent(accept.ElapsedTime, accept.IsSuccess, "WriteString", address, 1, value.ToString());
            }
            return accept;
        }
        #endregion

        #region Asynch

        public async Task<Package<bool>> AsyncReadBool(string address, byte function = 0x01)
        {
            Package<bool> accept = new Package<bool>();
            if (IsConnect)
            {
                if (function != 0x01 && function != 0x02)
                    throw new ArgumentException("byte function参数指定功能码错误");
                accept.SendBuff = GetReadMessages(function, address, 1);
                await AsyncSendReceive(accept);
                if (accept.IsSuccess)
                {
                    bool decimalValue = BitConverter.ToBoolean(accept.DataBuff, 0);
                    accept.Value = decimalValue;
                }
            }
            OnInteractionEvent(accept.ElapsedTime, accept.IsSuccess, "ReadBool", address, 1, accept.Value.ToString());
            return accept;
        }

        public async Task<Package<bool[]>> AsyncReadBool(string address, int length, byte function = 0x01)
        {
            Package<bool[]> accept = new Package<bool[]>();
            string result = "Null";
            if (IsConnect)
            {
                if (function != 0x01 && function != 0x02)
                    throw new ArgumentException("byte function参数指定功能码错误");
                accept.SendBuff = GetReadMessages(function, address, 1 * length);
                await AsyncSendReceive(accept);
                if (accept.IsSuccess)
                {
                    bool[] bools = new bool[length];
                    accept.DataBuff = ReverseBytes(accept.DataBuff);
                    bools = BytesToBitsReversed(accept.DataBuff, length);
                    accept.Value = bools;
                    result = string.Join(", ", accept.Value.Select((value, index) => $"{index}={value}"));
                }
            }
            OnInteractionEvent(accept.ElapsedTime, accept.IsSuccess, "ReadBool[]", address, length, result);
            return accept;
        }

        public async Task<Package<byte[]>> AsyncReadBytes(string address, int registerLength, byte function = 0x03)
        {
            Package<byte[]> accept = new Package<byte[]>();
            string result = "Null";
            if (IsConnect)
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("byte function参数指定功能码错误");
                accept.SendBuff = GetReadMessages(function, address, registerLength);
                await AsyncSendReceive(accept);
                if (accept.IsSuccess)
                {
                    accept.DataBuff = ReverseBytes(accept.DataBuff);
                    accept.Value = accept.DataBuff;
                    result = BitConverter.ToString(accept.Value);
                }
            }
            OnInteractionEvent(accept.ElapsedTime, accept.IsSuccess, "ReadBytes[]", address, registerLength, result);
            return accept;
        }

        public async Task<Package<short>> AsyncReadIn16(string address, byte function = 0x03)
        {
            Package<short> accept = new Package<short>();
            if (IsConnect)
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("byte function参数指定功能码错误");
                accept.SendBuff = GetReadMessages(function, address, 1);
                await AsyncSendReceive(accept);
                if (accept.IsSuccess)
                {
                    accept.DataBuff = ConvertByteOrderBatch(accept.DataBuff, 2, ByteOrder.ABCD);
                    short decimalValue = BitConverter.ToInt16(accept.DataBuff, 0);
                    accept.Value = decimalValue;
                }
            }
            OnInteractionEvent(accept.ElapsedTime, accept.IsSuccess, "ReadIn16", address, 1, accept.Value.ToString());
            return accept;
        }

        public async Task<Package<short[]>> AsyncReadIn16(string address, int length, byte function = 0x03)
        {
            Package<short[]> accept = new Package<short[]>();
            string result = "Null";
            if (IsConnect)
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("byte function参数指定功能码错误");
                accept.SendBuff = GetReadMessages(function, address, length * 1);
                await AsyncSendReceive(accept);
                if (accept.IsSuccess)
                {
                    accept.DataBuff = ConvertByteOrderBatch(accept.DataBuff, 2, ByteOrder.ABCD);
                    short[] shorts = new short[accept.DataBuff.Length / 2];
                    for (int i = 0; i < shorts.Length; i++)
                        shorts[shorts.Length - 1 - i] = BitConverter.ToInt16(accept.DataBuff, i * 2);
                    accept.Value = shorts;
                    result = string.Join(", ", accept.Value.Select((value, index) => $"{index}={value}"));
                }
            }
            OnInteractionEvent(accept.ElapsedTime, accept.IsSuccess, "ReadIn16[]", address, length, result);
            return accept;
        }

        public async Task<Package<int>> AsyncReadIn32(string address, byte function = 0x03)
        {
            Package<int> accept = new Package<int>();
            if (IsConnect)
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("byte function参数指定功能码错误");
                accept.SendBuff = GetReadMessages(function, address, 2);
                await AsyncSendReceive(accept);
                if (accept.IsSuccess)
                {
                    accept.DataBuff = ConvertByteOrderBatch(accept.DataBuff, 4, Order);
                    int decimalValue = BitConverter.ToInt32(accept.DataBuff, 0);
                    accept.Value = decimalValue;
                }
            }
            InteractionEvent?.BeginInvoke(accept.ElapsedTime, accept.IsSuccess, "ReadIn32", address, 2, accept.Value.ToString(), null, null);
            return accept;
        }

        public async Task<Package<int[]>> AsyncReadIn32(string address, int length, byte function = 0x03)
        {
            Package<int[]> accept = new Package<int[]>();
            string result = "Null";
            if (IsConnect)
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("byte function参数指定功能码错误");
                accept.SendBuff = GetReadMessages(function, address, length * 2);
                await AsyncSendReceive(accept);
                if (accept.IsSuccess)
                {
                    accept.DataBuff = ConvertByteOrderBatch(accept.DataBuff, 4, Order);
                    int[] ints = new int[accept.DataBuff.Length / 4];
                    for (int i = 0; i < ints.Length; i++)
                        ints[ints.Length - 1 - i] = BitConverter.ToInt32(accept.DataBuff, i * 4);
                    accept.Value = ints;
                    result = string.Join(", ", accept.Value.Select((value, index) => $"{index}= {value}"));
                }
            }
            OnInteractionEvent(accept.ElapsedTime, accept.IsSuccess, "ReadIn32[]", address, length, result);
            return accept;
        }

        public async Task<Package<float>> AsyncReadInFloat(string address, byte function = 0x03)
        {
            Package<float> accept = new Package<float>();
            if (IsConnect)
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("byte function参数指定功能码错误");
                accept.SendBuff = GetReadMessages(function, address, 2);
                await AsyncSendReceive(accept);
                if (accept.IsSuccess)
                {
                    accept.DataBuff = ConvertByteOrderBatch(accept.DataBuff, 4, Order);
                    float decimalValue = BitConverter.ToSingle(accept.DataBuff, 0);
                    accept.Value = decimalValue;
                }
            }
            OnInteractionEvent(accept.ElapsedTime, accept.IsSuccess, "ReadInFloat", address, 2, accept.Value.ToString());
            return accept;
        }

        public async Task<Package<float[]>> AsyncReadInFloat(string address, int length, byte function = 0x03)
        {
            Package<float[]> accept = new Package<float[]>();
            string result = "Null";
            if (IsConnect)
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("byte function参数指定功能码错误");
                accept.SendBuff = GetReadMessages(function, address, length * 2);
                await AsyncSendReceive(accept);
                if (accept.IsSuccess)
                {
                    accept.DataBuff = ConvertByteOrderBatch(accept.DataBuff, 4, Order);
                    float[] floats = new float[accept.DataBuff.Length / 4];
                    for (int i = 0; i < floats.Length; i++)
                        floats[floats.Length - 1 - i] = BitConverter.ToSingle(accept.DataBuff, i * 4);
                    accept.Value = floats;
                    result = string.Join(", ", accept.Value.Select((value, index) => $"{index}={value}"));
                }
            }
            OnInteractionEvent(accept.ElapsedTime, accept.IsSuccess, "ReadInFloat[]", address, length, result);
            return accept;
        }

        public async Task<Package<double>> AsyncReadInDouble(string address, byte function = 0x03)
        {
            Package<double> accept = new Package<double>();
            if (IsConnect)
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("byte function参数指定功能码错误");
                accept.SendBuff = GetReadMessages(function, address, 4);
                await AsyncSendReceive(accept);
                if (accept.IsSuccess)
                {
                    accept.DataBuff = ConvertByteOrderBatch(accept.DataBuff, 8, Order);
                    double decimalValue = BitConverter.ToDouble(accept.DataBuff, 0);
                    accept.Value = decimalValue;
                }
            }
            OnInteractionEvent(accept.ElapsedTime, accept.IsSuccess, "ReadInFloat", address, 4, accept.Value.ToString());
            return accept;
        }

        public async Task<Package<double[]>> AsyncReadInDouble(string address, int length, byte function = 0x03)
        {
            Package<double[]> accept = new Package<double[]>();
            string result = "Null";
            if (IsConnect)
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("byte function参数指定功能码错误");
                accept.SendBuff = GetReadMessages(function, address, length * 8);
                await AsyncSendReceive(accept);
                if (accept.IsSuccess)
                {
                    accept.DataBuff = ConvertByteOrderBatch(accept.DataBuff, 8, Order);
                    double[] floats = new double[accept.DataBuff.Length];
                    for (int i = 0; i < floats.Length; i++)
                        floats[floats.Length - 1 - i] = BitConverter.ToDouble(accept.DataBuff, i * 8);
                    accept.Value = floats;
                    result = string.Join(", ", accept.Value.Select((value, index) => $"{index}={value}"));
                }
            }
            OnInteractionEvent(accept.ElapsedTime, accept.IsSuccess, "ReadInFloat[]", address, length, result);
            return accept;
        }

        public async Task<Package<string>> AsyncReadInString(string address, int length, byte function = 0x03)
        {
            Package<string> accept = new Package<string>();
            if (IsConnect)
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("byte function参数指定功能码错误");
                accept.SendBuff = GetReadMessages(function, address, length * 2);
                await AsyncSendReceive(accept);
                if (accept.IsSuccess)
                {
                    var strbyte = accept.DataBuff.Skip(1).ToArray();
                    accept.DataBuff = strbyte;
                    string decimalValue = Encoding.ASCII.GetString(accept.DataBuff).Replace("\0", string.Empty);
                    accept.Value = decimalValue;
                }
            }
            OnInteractionEvent(accept.ElapsedTime, accept.IsSuccess, "ReadInString", address, length * 2, accept.Value?.ToString());
            return accept;
        }

        public async Task<Package<bool>> AsyncWriteBool(string address, bool value)
        {
            Package<bool> accept = new Package<bool>();
            if (IsConnect)
            {
                accept.SendBuff = new byte[1][];
                accept.SendBuff[0] = GetWriteMessage(0x05, address, new byte[] { (byte)(value ? 0xff : 0), 0 });
                await AsyncReceive(accept);
                accept.DataBuff = new byte[2] { accept.ReceiveBuff[0][accept.ReceiveBuff[0].Length - 2], accept.ReceiveBuff[0][accept.ReceiveBuff[0].Length - 1] };
            }
            OnInteractionEvent(accept.ElapsedTime, accept.IsSuccess, "WriteBool", address, 1, value.ToString());
            return accept;
        }

        public async Task<Package<bool[]>> AsyncWriteBool(string address, bool[] value)
        {
            Package<bool[]> accept = new Package<bool[]>();
            string result = "Null";
            if (IsConnect)
            {
                int length = value.Length;
                int byteLength = (length + 7) / 8;
                byte[] result_byte = new byte[3 + byteLength];
                result_byte[0] = (byte)(length >> 8);
                result_byte[1] = (byte)(length & 0xFF);
                result_byte[2] = (byte)(byteLength);
                for (int i = 0; i < byteLength; i++)
                {
                    for (byte j = 0; j < 8; j++)
                    {
                        var a = i * 8 + j;
                        if (a < value.Length)
                        {
                            if (value[a])
                                result_byte[i + 3] |= (byte)(1 << j);
                        }
                        else
                            break;
                    }
                }
                accept.SendBuff = new byte[1][];
                accept.SendBuff[0] = GetWriteMessage(0x0f, address, result_byte);
                await AsyncReceive(accept);
                if (accept.IsSuccess)
                {
                    accept.DataBuff = new byte[2] { accept.ReceiveBuff[0][accept.ReceiveBuff[0].Length - 2], accept.ReceiveBuff[0][accept.ReceiveBuff[0].Length - 1] };
                    result = string.Join(", ", value.Select((value_list, index) => $"{index}={value_list}"));
                }
            }
            OnInteractionEvent(accept.ElapsedTime, accept.IsSuccess, "WriteBool[]", address, 1, result);
            return accept;
        }

        public async Task<Package<short>> AsyncWriteIn16(string address, short value)
        {
            Package<short> accept = new Package<short>();
            if (IsConnect)
            {
                var send_ = ConvertByteOrderBatch(BitConverter.GetBytes(value), 2, Order);
                accept.SendBuff = new byte[1][];
                accept.SendBuff[0] = GetWriteMessage(0x06, address, send_);
                await AsyncReceive(accept);
                accept.DataBuff = new byte[2] { accept.ReceiveBuff[0][accept.ReceiveBuff[0].Length - 2], accept.ReceiveBuff[0][accept.ReceiveBuff[0].Length - 1] };
            }
            OnInteractionEvent(accept.ElapsedTime, accept.IsSuccess, "WriteIn16", address, 1, value.ToString());
            return accept;
        }

        public async Task<Package<int>> AsyncWriteIn32(string address, int value)
        {
            Package<int> accept = new Package<int>();
            if (IsConnect)
            {
                var send_ = ConvertByteOrderBatch(GetBytesBigEndian(value), 4, Order);
                byte[] bytes = new byte[3 + send_.Length];
                bytes[0] = 0x00;
                bytes[1] = (byte)(send_.Length / 2);
                bytes[2] = (byte)send_.Length;
                Array.Copy(send_, 0, bytes, 3, send_.Length);
                accept.SendBuff = new byte[1][];
                accept.SendBuff[0] = GetWriteMessage(0x10, address, bytes);
                await AsyncReceive(accept);
                accept.DataBuff = new byte[5];
                Array.Copy(accept.ReceiveBuff[0], accept.ReceiveBuff[0].Length - 5, accept.DataBuff, 0, accept.DataBuff.Length);
            }
            OnInteractionEvent(accept.ElapsedTime, accept.IsSuccess, "WriteIn32", address, 1, value.ToString());
            return accept;
        }

        public async Task<Package<float>> AsyncWriteFloat(string address, float value)
        {
            Package<float> accept = new Package<float>();
            if (IsConnect)
            {
                var send_ = ConvertByteOrderBatch(GetBytesBigEndian(value), 4, Order);
                byte[] bytes = new byte[3 + send_.Length];
                bytes[0] = 0x00;
                bytes[1] = (byte)(send_.Length / 2);
                bytes[2] = (byte)send_.Length;
                Array.Copy(send_, 0, bytes, 3, send_.Length);
                accept.SendBuff = new byte[1][];
                accept.SendBuff[0] = GetWriteMessage(0x10, address, bytes);
                await AsyncReceive(accept);
                accept.DataBuff = new byte[5];
                Array.Copy(accept.ReceiveBuff[0], accept.ReceiveBuff[0].Length - 5, accept.DataBuff, 0, accept.DataBuff.Length);
            }
            OnInteractionEvent(accept.ElapsedTime, accept.IsSuccess, "WriteFloat", address, 1, value.ToString());
            return accept;
        }

        public async Task<Package<double>> AsyncWriteDouble(string address, double value)
        {
            Package<double> accept = new Package<double>();
            if (IsConnect)
            {
                var t1 = BitConverter.GetBytes(value);
                var send_ = ConvertByteOrderBatch(BitConverter.GetBytes(value), 8, Order);
                byte[] temp = new byte[4];
                Array.Copy(send_, 0, temp, 0, 4); // 将前4个字节复制到临时数组
                Array.Copy(send_, 4, send_, 0, 4); // 将后4个字节复制到前面
                Array.Copy(temp, 0, send_, 4, 4); // 将临时数组中的前4个字节复制到后面

                byte[] bytes = new byte[3 + send_.Length];
                bytes[0] = 0x00;
                bytes[1] = (byte)(send_.Length / 2);
                bytes[2] = (byte)send_.Length;
                Array.Copy(send_, 0, bytes, 3, send_.Length);
                accept.SendBuff = new byte[1][];
                accept.SendBuff[0] = GetWriteMessage(0x10, address, bytes);
                await AsyncReceive(accept);
                accept.DataBuff = new byte[5];
                Array.Copy(accept.ReceiveBuff[0], accept.ReceiveBuff[0].Length - 5, accept.DataBuff, 0, accept.DataBuff.Length);
            }
            OnInteractionEvent(accept.ElapsedTime, accept.IsSuccess, "WriteDouble", address, 1, value.ToString());
            return accept;
        }

        public async Task<Package<string>> AsyncWriteString(string address, string value)
        {
            Package<string> accept = new Package<string>();
            if (IsConnect)
            {
                var send_ = Encoding.ASCII.GetBytes(value);
                byte[] bytes = new byte[3 + send_.Length];
                bytes[0] = 0x00;
                bytes[1] = (byte)(send_.Length / 2);
                bytes[2] = (byte)send_.Length;
                Array.Copy(send_, 0, bytes, 3, send_.Length);
                accept.SendBuff = new byte[1][];
                accept.SendBuff[0] = GetWriteMessage(0x10, address, bytes);
                await AsyncReceive(accept);
                accept.DataBuff = new byte[5];
                Array.Copy(accept.ReceiveBuff[0], accept.ReceiveBuff[0].Length - 5, accept.DataBuff, 0, accept.DataBuff.Length);
            }
            OnInteractionEvent(accept.ElapsedTime, accept.IsSuccess, "WriteString", address, 1, value.ToString());
            return accept;
        }

        #endregion

        /// <summary>
        /// TCP断开
        /// </summary>
        public void Close()
        {
            if (tcpClient != null && tcpClient.Client.Connected)
                tcpClient.Close();
            if (!tcpClient.Client.Connected)
            {
                tcpClient.Close();
            }
        }
    }
}
