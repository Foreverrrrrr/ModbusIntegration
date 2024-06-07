using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.ConstrainedExecution;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ModbusIntegration.Modbus
{
    public class AsyncSharpTcpClient
    {
        public enum ByteOrder
        {
            ABCD, BADC, CDAB, DCBA
        }
        public ByteOrder Order = ByteOrder.BADC;
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

        private System.Net.Sockets.TcpClient tcpClient { get; set; }

        /// <summary>
        /// 站号
        /// </summary>
        public ushort StationNumber { get; set; } = 1;

        /// <summary>
        /// 连接服务器
        /// </summary>
        /// <param name="targetip">服务器ip</param>
        /// <param name="targetport">服务器端口号</param>
        public AsyncSharpTcpClient(string targetip, int targetport)
        {
            sendLock = new SemaphoreSlim(1, 1);
            AsyncNewTcp(targetip, targetport);
        }

        private void AsyncNewTcp(string targetip, int targetport)
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

        /// <summary>
        /// 异步数据发送，等待数据返回
        /// </summary>
        /// <param name="msg">发送字符串</param>
        /// <param name="encoding">字符串编码</param>
        /// <returns>返回字符串</returns>
        public void SyncSendReceive<T>(ref Package<T> syncSend)
        {
            try
            {
                sendLock.Wait();
                if (IsConnect)
                {
                    DateTime startTime = DateTime.Now;
                    tcpClient.GetStream().Write(syncSend.SendBuff, 0, syncSend.SendBuff.Length);
                    int bytesRead = tcpClient.GetStream().Read(ReadBuffer, 0, ReadBuffer.Length);
                    syncSend.ReceiveBuff = new byte[bytesRead];
                    Array.Copy(ReadBuffer, 0, syncSend.ReceiveBuff, 0, bytesRead);
                    if (bytesRead > 0)
                    {
                        if (ReadBuffer[0] != syncSend.SendBuff[0] && ReadBuffer[1] != syncSend.SendBuff[1] && ReadBuffer[7] != syncSend.SendBuff[7])
                        {
                            syncSend.IsSuccess = false;
                        }
                        else
                        {
                            syncSend.FunctionCode = ReadBuffer[7];
                            syncSend.DataBuff = new byte[syncSend.ReceiveBuff[8]];
                            for (int i = 0; i < syncSend.DataBuff.Length; i++)
                            {
                                syncSend.DataBuff[i] = syncSend.ReceiveBuff[9 + i];
                            }
                            syncSend.IsSuccess = true;
                        }
                    }
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

        public static byte[] ConvertByteOrder(byte[] bytes, ByteOrder byteOrder)
        {
            if (bytes.Length % 2 != 0)
            {
                throw new ArgumentException("字节数组长度必须为偶数。");
            }
            byte[] result = new byte[bytes.Length];
            switch (byteOrder)
            {
                case ByteOrder.ABCD:
                    Array.Copy(bytes, result, bytes.Length);
                    break;
                case ByteOrder.BADC:
                    for (int i = 0; i < bytes.Length; i += 2)
                    {
                        result[i] = bytes[i + 1];
                        result[i + 1] = bytes[i];
                    }
                    break;
                case ByteOrder.CDAB:
                    for (int i = 0; i < bytes.Length; i += 4)
                    {
                        result[i] = bytes[i + 2];
                        result[i + 1] = bytes[i + 3];
                        result[i + 2] = bytes[i];
                        result[i + 3] = bytes[i + 1];
                    }
                    break;
                case ByteOrder.DCBA:
                    for (int i = 0; i < bytes.Length; i += 4)
                    {
                        result[i] = bytes[i + 3];
                        result[i + 1] = bytes[i + 2];
                        result[i + 2] = bytes[i + 1];
                        result[i + 3] = bytes[i];
                    }
                    break;
                default:
                    throw new ArgumentException("不支持的字节顺序枚举值。");
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

        public Package<bool> ReadBool(string address, byte function = 0x01)
        {
            Package<bool> accept = new Package<bool>();
            if (IsConnect)
            {
                if (function != 0x01 && function != 0x02)
                    throw new ArgumentException("byte function参数指定功能码错误");
                accept.SendBuff = GetReadMessage(function, address, 1);
                SyncSendReceive(ref accept);
                if (accept.IsSuccess)
                {
                    bool decimalValue = BitConverter.ToBoolean(accept.DataBuff, 0);
                    accept.Value = decimalValue;
                }
            }
            InteractionEvent?.BeginInvoke(accept.ElapsedTime, accept.IsSuccess, "ReadBool", address, 1, accept.Value.ToString(), null, null);
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
                accept.SendBuff = GetReadMessage(function, address, 1 * length);
                SyncSendReceive(ref accept);
                if (accept.IsSuccess)
                {
                    bool[] bools = new bool[length];
                    bools = BytesToBitsReversed(accept.DataBuff, length);
                    accept.Value = bools;
                    result = string.Join(", ", accept.Value.Select((value, index) => $"{index}={value}"));
                }
            }
            InteractionEvent?.BeginInvoke(accept.ElapsedTime, accept.IsSuccess, "ReadBool[]", address, length, result, null, null);
            return accept;
        }

        public Package<short> ReadIn16(string address, byte function = 0x03)
        {
            Package<short> accept = new Package<short>();
            if (IsConnect)
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("byte function参数指定功能码错误");
                accept.SendBuff = GetReadMessage(function, address, 1);
                SyncSendReceive(ref accept);
                if (accept.IsSuccess)
                {
                    accept.DataBuff = ConvertByteOrder(accept.DataBuff, Order);
                    short decimalValue = BitConverter.ToInt16(accept.DataBuff, 0);
                    accept.Value = decimalValue;
                }
            }
            InteractionEvent?.BeginInvoke(accept.ElapsedTime, accept.IsSuccess, "ReadIn16", address, 1, accept.Value.ToString(), null, null);
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
                accept.SendBuff = GetReadMessage(function, address, length * 1);
                SyncSendReceive(ref accept);
                if (accept.IsSuccess)
                {
                    accept.DataBuff = ConvertByteOrder(accept.DataBuff, Order);
                    short[] shorts = new short[accept.DataBuff.Length / 2];
                    for (int i = 0; i < shorts.Length; i++)
                        shorts[i] = BitConverter.ToInt16(accept.DataBuff, i * 2);
                    accept.Value = shorts;
                    result = string.Join(", ", accept.Value.Select((value, index) => $"{index}={value}"));
                }
            }
            InteractionEvent?.BeginInvoke(accept.ElapsedTime, accept.IsSuccess, "ReadIn16[]", address, length, result, null, null);
            return accept;
        }

        public Package<int> ReadIn32(string address, byte function = 0x03)
        {
            Package<int> accept = new Package<int>();
            if (IsConnect)
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("byte function参数指定功能码错误");
                accept.SendBuff = GetReadMessage(function, address, 2);
                SyncSendReceive(ref accept);
                if (accept.IsSuccess)
                {
                    accept.DataBuff = ConvertByteOrder(accept.DataBuff, Order);
                    int decimalValue = BitConverter.ToInt32(accept.DataBuff, 0);
                    accept.Value = decimalValue;
                }
            }
            InteractionEvent?.BeginInvoke(accept.ElapsedTime, accept.IsSuccess, "ReadIn32", address, 2, accept.Value.ToString(), null, null);
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
                accept.SendBuff = GetReadMessage(function, address, length * 2);
                SyncSendReceive(ref accept);
                if (accept.IsSuccess)
                {
                    accept.DataBuff = ConvertByteOrder(accept.DataBuff, Order);
                    int[] ints = new int[accept.DataBuff.Length / 4];
                    for (int i = 0; i < ints.Length; i++)
                        ints[i] = BitConverter.ToInt32(accept.DataBuff, i * 4);
                    accept.Value = ints;
                    result = string.Join(", ", accept.Value.Select((value, index) => $"{index}= {value}"));
                }
            }
            InteractionEvent?.BeginInvoke(accept.ElapsedTime, accept.IsSuccess, "ReadIn32[]", address, length, result, null, null);
            return accept;
        }


        public Package<float> ReadInFloat(string address, byte function = 0x03)
        {
            Package<float> accept = new Package<float>();
            if (IsConnect)
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("byte function参数指定功能码错误");
                accept.SendBuff = GetReadMessage(function, address, 2);
                SyncSendReceive(ref accept);
                if (accept.IsSuccess)
                {
                    accept.DataBuff = ConvertByteOrder(accept.DataBuff, Order);
                    float decimalValue = BitConverter.ToSingle(accept.DataBuff, 0);
                    accept.Value = decimalValue;
                }
            }
            InteractionEvent?.BeginInvoke(accept.ElapsedTime, accept.IsSuccess, "ReadInFloat", address, 2, accept.Value.ToString(), null, null);
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
                accept.SendBuff = GetReadMessage(function, address, length * 2);
                SyncSendReceive(ref accept);
                if (accept.IsSuccess)
                {
                    accept.DataBuff = ConvertByteOrder(accept.DataBuff, Order);
                    float[] floats = new float[accept.DataBuff.Length / 4];
                    for (int i = 0; i < floats.Length; i++)
                        floats[i] = BitConverter.ToInt32(accept.DataBuff, i * 4);
                    accept.Value = floats;
                    result = string.Join(", ", accept.Value.Select((value, index) => $"{index}={value}"));
                }
            }
            InteractionEvent?.BeginInvoke(accept.ElapsedTime, accept.IsSuccess, "ReadInFloat[]", address, length, result, null, null);
            return accept;
        }

        public Package<double> ReadInDouble(string address, byte function = 0x03)
        {
            Package<double> accept = new Package<double>();
            if (IsConnect)
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("byte function参数指定功能码错误");
                accept.SendBuff = GetReadMessage(function, address, 4);
                SyncSendReceive(ref accept);
                if (accept.IsSuccess)
                {
                    accept.DataBuff = ConvertByteOrder(accept.DataBuff, Order);
                    double decimalValue = BitConverter.ToDouble(accept.DataBuff, 0);
                    accept.Value = decimalValue;
                }
            }
            InteractionEvent?.BeginInvoke(accept.ElapsedTime, accept.IsSuccess, "ReadInFloat", address, 4, accept.Value.ToString(), null, null);
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
                accept.SendBuff = GetReadMessage(function, address, length * 4);
                SyncSendReceive(ref accept);
                if (accept.IsSuccess)
                {
                    accept.DataBuff = ConvertByteOrder(accept.DataBuff, Order);
                    double[] floats = new double[accept.DataBuff.Length / 8];
                    for (int i = 0; i < floats.Length; i++)
                        floats[i] = BitConverter.ToDouble(accept.DataBuff, i * 8);
                    accept.Value = floats;
                    result = string.Join(", ", accept.Value.Select((value, index) => $"{index}={value}"));
                }
            }
            InteractionEvent?.BeginInvoke(accept.ElapsedTime, accept.IsSuccess, "ReadInFloat[]", address, length, result, null, null);
            return accept;
        }

        public Package<string> ReadInString(string address, int length, byte function = 0x03)
        {
            Package<string> accept = new Package<string>();
            if (IsConnect)
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("byte function参数指定功能码错误");
                accept.SendBuff = GetReadMessage(function, address, length * 2);
                SyncSendReceive(ref accept);
                if (accept.IsSuccess)
                {
                    string decimalValue = Encoding.ASCII.GetString(accept.DataBuff).Replace("\0", "");
                    accept.Value = decimalValue;
                }
            }
            InteractionEvent?.BeginInvoke(accept.ElapsedTime, accept.IsSuccess, "ReadInString", address, length * 2, accept.Value?.ToString(), null, null);
            return accept;
        }

        public Package<bool> WriteBool(string address, bool value)
        {
            Package<bool> accept = new Package<bool>();
            if (IsConnect)
            {
                accept.SendBuff = GetWriteMessage(0x05, address, new byte[] { (byte)(value ? 0xff : 0), 0 });
                SyncSendReceive(ref accept);
                accept.DataBuff = new byte[2] { accept.ReceiveBuff[accept.ReceiveBuff.Length - 2], accept.ReceiveBuff[accept.ReceiveBuff.Length - 1] };
            }
            InteractionEvent?.BeginInvoke(accept.ElapsedTime, accept.IsSuccess, "WriteBool", address, 1, value.ToString(), null, null);
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
                accept.SendBuff = GetWriteMessage(0x0f, address, result_byte);
                SyncSendReceive(ref accept);
                if (accept.IsSuccess)
                {
                    accept.DataBuff = new byte[2] { accept.ReceiveBuff[accept.ReceiveBuff.Length - 2], accept.ReceiveBuff[accept.ReceiveBuff.Length - 1] };
                    result = string.Join(", ", value.Select((value_list, index) => $"{index}={value_list}"));
                }
            }
            InteractionEvent?.BeginInvoke(accept.ElapsedTime, accept.IsSuccess, "WriteBool[]", address, 1, result, null, null);
            return accept;
        }

        public Package<short> WriteIn16(string address, short value)
        {
            Package<short> accept = new Package<short>();
            if (IsConnect)
            {
                var send_ = ConvertByteOrder(BitConverter.GetBytes(value), Order);
                accept.SendBuff = GetWriteMessage(0x06, address, send_);
                SyncSendReceive(ref accept);
                accept.DataBuff = new byte[2] { accept.ReceiveBuff[accept.ReceiveBuff.Length - 2], accept.ReceiveBuff[accept.ReceiveBuff.Length - 1] };
            }
            InteractionEvent?.BeginInvoke(accept.ElapsedTime, accept.IsSuccess, "WriteIn16", address, 1, value.ToString(), null, null);
            return accept;
        }

        public Package<int> WriteIn32(string address, int value)
        {
            Package<int> accept = new Package<int>();
            if (IsConnect)
            {
                var send_ = ConvertByteOrder(BitConverter.GetBytes(value), Order);
                accept.SendBuff = GetWriteMessage(0x06, address, send_);
                SyncSendReceive(ref accept);
                accept.DataBuff = new byte[2] { accept.ReceiveBuff[accept.ReceiveBuff.Length - 2], accept.ReceiveBuff[accept.ReceiveBuff.Length - 1] };
            }
            InteractionEvent?.BeginInvoke(accept.ElapsedTime, accept.IsSuccess, "WriteIn16", address, 1, value.ToString(), null, null);
            return accept;
        }

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
