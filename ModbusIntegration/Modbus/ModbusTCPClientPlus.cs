using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ModbusIntegration.Modbus
{
    /// <summary>
    /// Modbus TCP 客户端
    /// </summary>
    public class ModbusTCPClientPlus : IDisposable
    {
        #region 枚举和常量

        public enum ByteOrder
        {
            ABCD, BADC, CDAB, DCBA
        }

        /// <summary>
        /// 触发器数据类型
        /// </summary>
        public enum TriggerDataType
        {
            Bit,
            Bool,
            Int16,
            UInt16,
            Int32,
            UInt32,
            Int64,
            Float,
            Double,
            String
        }

        /// <summary>
        /// 触发条件类型
        /// </summary>
        public enum TriggerCondition
        {
            /// <summary>
            /// 等于触发值
            /// </summary>
            Equal,

            /// <summary>
            /// 不等于触发值
            /// </summary>
            NotEqual,

            /// <summary>
            /// 大于触发值
            /// </summary>
            GreaterThan,

            /// <summary>
            /// 小于触发值
            /// </summary>
            LessThan,

            /// <summary>
            /// 大于等于触发值
            /// </summary>
            GreaterThanOrEqual,

            /// <summary>
            /// 小于等于触发值
            /// </summary>
            LessThanOrEqual,

            /// <summary>
            /// 值发生变化
            /// </summary>
            Changed,
        }

        /// <summary>
        /// Modbus 功能码
        /// </summary>
        public static class FunctionCode
        {
            public const byte ReadCoils = 0x01;
            public const byte ReadDiscreteInputs = 0x02;
            public const byte ReadHoldingRegisters = 0x03;
            public const byte ReadInputRegisters = 0x04;
            public const byte WriteSingleCoil = 0x05;
            public const byte WriteSingleRegister = 0x06;
            public const byte WriteMultipleCoils = 0x0F;
            public const byte WriteMultipleRegisters = 0x10;
        }

        /// <summary>
        /// Modbus 异常码
        /// </summary>
        public static class ExceptionCode
        {
            public const byte IllegalFunction = 0x01;
            public const byte IllegalDataAddress = 0x02;
            public const byte IllegalDataValue = 0x03;
            public const byte ServerDeviceFailure = 0x04;
            public const byte Acknowledge = 0x05;
            public const byte ServerDeviceBusy = 0x06;
            public const byte MemoryParityError = 0x08;
            public const byte GatewayPathUnavailable = 0x0A;
            public const byte GatewayTargetDeviceFailedToRespond = 0x0B;

            public static string GetDescription(byte code)
            {
                switch (code)
                {
                    case IllegalFunction: return "非法功能码";
                    case IllegalDataAddress: return "非法数据地址";
                    case IllegalDataValue: return "非法数据值";
                    case ServerDeviceFailure: return "服务器设备故障";
                    case Acknowledge: return "确认";
                    case ServerDeviceBusy: return "服务器设备忙";
                    case MemoryParityError: return "存储器奇偶校验错误";
                    case GatewayPathUnavailable: return "网关路径不可用";
                    case GatewayTargetDeviceFailedToRespond: return "网关目标设备无响应";
                    default: return $"未知异常码: 0x{code:X2}";
                }
            }
        }

        private const int DEFAULT_TIMEOUT_MS = 5000;
        private const int MAX_RECONNECT_ATTEMPTS = 3;
        private const int RECONNECT_DELAY_MS = 1000;
        private const int MAX_REGISTERS_PER_READ = 125;
        private const int RECEIVE_BUFFER_SIZE = 1024 * 64;
        private const int DEFAULT_TRIGGER_POLL_INTERVAL_MS = 100;
        private const int DEFAULT_HEARTBEAT_INTERVAL_MS = 1000;
        private const int DEFAULT_RECONNECT_INTERVAL_MS = 3000;
        private const int DEFAULT_MAX_RECONNECT_ATTEMPTS = 0;
        private const int DEFAULT_THEN_DELAY_MS = 30;

        #endregion

        #region 触发器配置类

        /// <summary>
        /// 连带读取配置
        /// </summary>
        public class LinkedReadConfig
        {
            /// <summary>寄存器地址</summary>
            public string Address { get; set; }

            /// <summary>数据类型</summary>
            public TriggerDataType DataType { get; set; }

            /// <summary>读取长度（数组/字符串时使用，默认1）</summary>
            public int Length { get; set; } = 1;

            /// <summary>标签</summary>
            public string Name { get; set; }
        }

        /// <summary>
        /// 连带读取结果
        /// </summary>
        public class LinkedReadResult
        {
            /// <summary>寄存器地址</summary>
            public string Address { get; set; }

            /// <summary>标签名</summary>
            public string Name { get; set; }

            /// <summary>数据类型</summary>
            public TriggerDataType DataType { get; set; }

            /// <summary>读取到的值</summary>
            public object Value { get; set; }

            /// <summary>是否读取成功</summary>
            public bool IsSuccess { get; set; }

            /// <summary>失败时的错误信息</summary>
            public string ErrorMessage { get; set; }
        }

        /// <summary>
        /// 触发器配置
        /// </summary>
        public class TriggerConfig
        {
            /// <summary>
            /// 触发器唯一标识
            /// </summary>
            public string Id { get; set; } = Guid.NewGuid().ToString("N");

            /// <summary>
            /// 触发器名称
            /// </summary>
            public string Name { get; set; }

            /// <summary>
            /// 监控地址
            /// </summary>
            public string Address { get; set; }

            /// <summary>
            /// 数据类型
            /// </summary>
            public TriggerDataType DataType { get; set; }

            /// <summary>
            /// 触发条件
            /// </summary>
            public TriggerCondition Condition { get; set; } = TriggerCondition.Equal;

            /// <summary>
            /// 触发值
            /// </summary>
            public string TriggerValue { get; set; }

            /// <summary>
            /// 字符串读取长度
            /// </summary>
            public int TriggerValueLength { get; set; }

            /// <summary>
            /// 功能码（0x01/0x02 用于Bool，0x03/0x04 用于寄存器）
            /// </summary>
            public byte FunctionCode { get; set; } = 0x03;

            /// <summary>
            /// 是否启用
            /// </summary>
            public bool IsEnabled { get; set; } = true;

            /// <summary>
            /// 用户自定义标签
            /// </summary>
            public object Tag { get; set; }

            /// <summary>
            /// 触发回调
            /// </summary>
            public Action<TriggerEventArgs> Callback { get; set; }

            /// <summary>
            /// Callback完成操作委托
            /// </summary>
            public Action<ModbusTCPClientPlus> ThenAction { get; set; }

            /// <summary>
            /// 连带读取列表
            /// </summary>
            public List<LinkedReadConfig> LinkedReads { get; set; } = new List<LinkedReadConfig>();

            /// <summary>
            /// 上一次读取的值
            /// </summary>
            internal object LastValue { get; set; }

            /// <summary>
            /// 是否已经触发过
            /// </summary>
            internal bool HasTriggered { get; set; }

            /// <summary>
            /// Callback 执行完成回调
            /// </summary>
            /// <param name="thenAction"> ModbusTCPClientPlus </param>
            /// <returns></returns>
            public TriggerConfig Then(Action<ModbusTCPClientPlus> thenAction)
            {
                this.ThenAction = thenAction;
                return this;
            }

            /// <summary>
            /// 添加连带读取地址，触发满足条件自动读取该地址
            /// </summary>
            /// <param name="address">寄存器地址</param>
            /// <param name="dataType">数据类型</param>
            /// <param name="name">标签，回调识别</param>
            /// <param name="length">数组/字符串长度</param>
            /// <returns></returns>
            public TriggerConfig Link(string address, TriggerDataType dataType, string name = null, int length = 1)
            {
                LinkedReads.Add(new LinkedReadConfig
                {
                    Address = address,
                    DataType = dataType,
                    Name = name ?? address,
                    Length = length
                });
                return this;
            }
        }

        /// <summary>
        /// 触发事件参数
        /// </summary>
        public class TriggerEventArgs : EventArgs
        {
            /// <summary>
            /// ModbusTCPClientPlus 实例
            /// </summary>
            public ModbusTCPClientPlus Client { get; set; }

            /// <summary>
            /// 触发器配置
            /// </summary>
            public TriggerConfig Trigger { get; set; }

            /// <summary>
            /// 触发时的当前值
            /// </summary>
            public object CurrentValue { get; set; }

            /// <summary>
            /// 触发时的上一次值
            /// </summary>
            public object PreviousValue { get; set; }

            /// <summary>
            /// 触发时间
            /// </summary>
            public DateTime TriggerTime { get; set; }

            /// <summary>
            /// 是否读取成功
            /// </summary>
            public bool IsSuccess { get; set; }

            /// <summary>
            /// 失败错误信息
            /// </summary>
            public string ErrorMessage { get; set; }

            /// <summary>
            /// 连带读取结果列表
            /// </summary>
            public List<LinkedReadResult> LinkedValues { get; set; }

            /// <summary>
            /// Get LinkedValues
            /// </summary>
            /// <typeparam name="T">目标类型</typeparam>
            /// <param name="nameOrAddress">Link 时设置的 Name，或寄存器地址</param>
            /// <returns></returns>
            public T GetLinkedValue<T>(string nameOrAddress)
            {
                if (LinkedValues == null || string.IsNullOrEmpty(nameOrAddress))
                    return default;

                var item = LinkedValues.FirstOrDefault(v =>
                    string.Equals(v.Name, nameOrAddress, StringComparison.OrdinalIgnoreCase))
                ?? LinkedValues.FirstOrDefault(v =>
                    string.Equals(v.Address, nameOrAddress, StringComparison.OrdinalIgnoreCase));
                if (item == null || !item.IsSuccess || item.Value == null)
                    return default;
                try
                {
                    return (T)Convert.ChangeType(item.Value, typeof(T));
                }
                catch
                {
                    return default;
                }
            }

            /// <summary>
            /// 获取连带读取值
            /// </summary>
            /// <typeparam name="T">目标类型</typeparam>
            /// <param name="nameOrAddress">Link 时设置的 Name，或寄存器地址</param>
            /// <param name="value">输出值</param>
            /// <returns>是否成功获取</returns>
            public bool TryGetLinkedValue<T>(string nameOrAddress, out T value)
            {
                value = default;
                if (LinkedValues == null || string.IsNullOrEmpty(nameOrAddress))
                    return false;

                var item = LinkedValues.FirstOrDefault(v =>
                    string.Equals(v.Name, nameOrAddress, StringComparison.OrdinalIgnoreCase))
                ?? LinkedValues.FirstOrDefault(v =>
                    string.Equals(v.Address, nameOrAddress, StringComparison.OrdinalIgnoreCase));

                if (item == null || !item.IsSuccess || item.Value == null)
                    return false;

                try
                {
                    value = (T)Convert.ChangeType(item.Value, typeof(T));
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        #endregion

        #region 私有字段

        private readonly SemaphoreSlim _sendLock;
        private readonly byte[] _readBuffer;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly List<TriggerConfig> _triggers;
        private readonly object _triggerLock = new object();

        private TcpClient _tcpClient;
        private int _transactionId;
        private int _reconnectAttempts;
        private volatile bool _isDisposed;
        private volatile bool _isTriggerMonitorRunning;
        private Task _triggerMonitorTask;
        private CancellationTokenSource _triggerCancellationTokenSource;

        private volatile bool _isReconnecting;
        private volatile bool _lastKnownConnectedState;
        private volatile bool _userRequestedDisconnect;
        private Task _heartbeatTask;
        private CancellationTokenSource _heartbeatCts;
        private CancellationTokenSource _reconnectCts;
        private readonly object _reconnectLock = new object();
        private int _connectionLostHandled = 0;

        #endregion

        #region 公共属性

        /// <summary>
        /// 字节序
        /// </summary>
        public ByteOrder Order { get; set; } = ByteOrder.CDAB;

        /// <summary>
        /// 服务器IP
        /// </summary>
        public string TargetIP { get; private set; }

        /// <summary>
        /// 服务器端口号
        /// </summary>
        public int TargetPort { get; private set; }

        /// <summary>
        /// 连接状态
        /// </summary>
        public bool IsConnected => _tcpClient?.Connected == true && !_isDisposed;

        /// <summary>
        /// 站号
        /// </summary>
        public ushort StationNumber { get; set; } = 1;

        /// <summary>
        /// 首地址 false从0开始 true从1开始
        /// </summary>
        public bool AddressStartsAtOne { get; set; } = false;

        /// <summary>
        /// 连接超时时间（毫秒）
        /// </summary>
        public int ConnectionTimeout { get; set; } = DEFAULT_TIMEOUT_MS;

        /// <summary>
        /// 是否启用自动重连（默认启用）
        /// </summary>
        public bool AutoReconnect { get; set; } = true;

        /// <summary>
        /// 是否启用心跳检测（默认启用）
        /// </summary>
        public bool EnableHeartbeat { get; set; } = true;

        /// <summary>
        /// 心跳检测间隔（毫秒），默认3000ms
        /// </summary>
        public int HeartbeatInterval { get; set; } = DEFAULT_HEARTBEAT_INTERVAL_MS;

        /// <summary>
        /// 重连间隔（毫秒），默认3000ms
        /// </summary>
        public int ReconnectInterval { get; set; } = DEFAULT_RECONNECT_INTERVAL_MS;

        /// <summary>
        /// 最大重连次数，0表示无限重连
        /// </summary>
        public int MaxReconnectAttempts { get; set; } = DEFAULT_MAX_RECONNECT_ATTEMPTS;

        /// <summary>
        /// 是否正在重连中
        /// </summary>
        public bool IsReconnecting => _isReconnecting;

        /// <summary>
        /// 读取超时时间（毫秒）
        /// </summary>
        public int ReadTimeout { get; set; } = DEFAULT_TIMEOUT_MS;

        /// <summary>
        /// 触发器轮询间隔（毫秒）
        /// </summary>
        public int TriggerPollInterval { get; set; } = DEFAULT_TRIGGER_POLL_INTERVAL_MS;

        /// <summary>
        /// Callback 完成后到执行 Then 之间的延迟，默认20
        /// </summary>
        public int ThenDelayMs { get; set; } = DEFAULT_THEN_DELAY_MS;

        /// <summary>
        /// 心跳检测读取的 Modbus 地址（默认 "0"）
        /// </summary>
        public string HeartbeatAddress { get; set; } = "0";

        /// <summary>
        /// 心跳检测使用的功能码：0x01/0x02 读线圈/离散，0x03/0x04 读保持/输入寄存器（默认 0x03）
        /// </summary>
        public byte HeartbeatFunctionCode { get; set; } = 0x03;

        /// <summary>
        /// 触发监控是否正在运行
        /// </summary>
        public bool IsTriggerMonitorRunning => _isTriggerMonitorRunning;

        /// <summary>
        /// 获取当前触发器数量
        /// </summary>
        public int TriggerCount
        {
            get
            {
                lock (_triggerLock)
                {
                    return _triggers.Count;
                }
            }
        }
        /// <summary>
        /// 字符串颠倒
        /// </summary>
        public bool ReverseString { get; set; } = false;

        #endregion

        #region 事件

        /// <summary>
        /// 断开连接事件：时间，服务器IP，服务器端口，异常信息
        /// </summary>
        public event Action<DateTime, string, int, Exception> DisconnectionEvent;

        /// <summary>
        /// 连接成功事件：时间，服务器IP，服务器端口
        /// </summary>
        public event Action<DateTime, string, int> SuccessfulConnectEvent;

        /// <summary>
        /// 交互事件：耗时，操作结果，读取/写入，起始地址，寄存器数，操作值
        /// </summary>
        public event Action<TimeSpan, bool, string, string, int, string> InteractionEvent;

        /// <summary>
        /// 触发事件
        /// </summary>
        public event EventHandler<TriggerEventArgs> TriggerEvent;

        #endregion

        #region 构造函数

        /// <summary>
        /// 默认构造函数
        /// </summary>
        public ModbusTCPClientPlus()
        {
            _sendLock = new SemaphoreSlim(1, 1);
            _readBuffer = new byte[RECEIVE_BUFFER_SIZE];
            _cancellationTokenSource = new CancellationTokenSource();
            _triggers = new List<TriggerConfig>();
        }

        /// <summary>
        /// 连接到指定服务器
        /// </summary>
        /// <param name="targetIP">服务器IP</param>
        /// <param name="targetPort">服务器端口</param>
        public ModbusTCPClientPlus(string targetIP, int targetPort)
            : this(targetIP, targetPort, ByteOrder.CDAB)
        {
        }

        /// <summary>
        /// 连接到指定服务器并设置字节序
        /// </summary>
        /// <param name="targetIP">服务器IP</param>
        /// <param name="targetPort">服务器端口</param>
        /// <param name="order">字节序</param>
        /// <remarks>
        /// </remarks>
        public ModbusTCPClientPlus(string targetIP, int targetPort, ByteOrder order) : this()
        {
            Order = order;
            TargetIP = targetIP;
            TargetPort = targetPort;
            AsyncNewTcp(targetIP, targetPort);
        }

        #endregion

        #region 连接管理

        /// <summary>
        /// 异步连接到Modbus TCP服务器
        /// </summary>
        public bool AsyncNewTcp(string targetip, int targetport)
        {
            this.TargetIP = targetip;
            this.TargetPort = targetport;
            _userRequestedDisconnect = false;
            Task.Run(() => ConnectWithRetryAsync());
            return true;
        }

        /// <summary>
        /// 带重试的连接方法
        /// </summary>
        private async Task ConnectWithRetryAsync(bool notifyOnFirstFailure = true)
        {
            lock (_reconnectLock)
            {
                if (_isDisposed || _userRequestedDisconnect) return;
                if (_isReconnecting) return;
                _isReconnecting = true;
            }
            _reconnectAttempts = 0;

            try { _reconnectCts?.Cancel(); _reconnectCts?.Dispose(); } catch { }
            _reconnectCts = new CancellationTokenSource();
            var reconnectToken = _reconnectCts.Token;

            try
            {
                while (!_isDisposed && !_userRequestedDisconnect && !reconnectToken.IsCancellationRequested)
                {
                    _reconnectAttempts++;
                    if (MaxReconnectAttempts > 0 && _reconnectAttempts >= MaxReconnectAttempts)
                    {
                        OnDisconnection(new Exception($"已达到最大重连次数({MaxReconnectAttempts})，停止重连"));
                        break;
                    }
                    try
                    {
                        CloseExistingConnection();
                        if (_reconnectAttempts > 1)
                        {
                            System.Diagnostics.Debug.WriteLine($"正在尝试第 {_reconnectAttempts - 1} 次重连到 {TargetIP}:{TargetPort}...");
                        }
                        _tcpClient = new TcpClient();
                        if (!IPAddress.TryParse(TargetIP, out var ipAddress))
                        {
                            OnDisconnection(new FormatException($"无效的IP地址: \"{TargetIP}\"，停止重连"));
                            break;
                        }
                        var connectTask = _tcpClient.ConnectAsync(ipAddress, TargetPort);
                        var timeoutTask = Task.Delay(ConnectionTimeout, reconnectToken);
                        var completedTask = await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false);
                        reconnectToken.ThrowIfCancellationRequested();
                        if (completedTask == timeoutTask)
                        {
                            CloseExistingConnection();
                            throw new TimeoutException($"连接 {TargetIP}:{TargetPort} 超时({ConnectionTimeout}ms)");
                        }
                        await connectTask.ConfigureAwait(false);
                        if (_tcpClient != null && _tcpClient.Connected)
                        {
                            _tcpClient.ReceiveTimeout = ReadTimeout;
                            _tcpClient.SendTimeout = ReadTimeout;
                            _reconnectAttempts = 0;
                            _isReconnecting = false;
                            _lastKnownConnectedState = true;
                            Interlocked.Exchange(ref _connectionLostHandled, 0);

                            OnSuccessfulConnect();
                            StartHeartbeat();
                            return;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"连接失败: {ex.Message}");
                        if (_reconnectAttempts == 1 && notifyOnFirstFailure)
                        {
                            OnDisconnection(ex);
                        }
                    }
                    if (!AutoReconnect)
                    {
                        break;
                    }
                    try
                    {
                        await Task.Delay(ReconnectInterval, reconnectToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
            finally
            {
                _isReconnecting = false;
                try { _reconnectCts?.Dispose(); _reconnectCts = null; } catch { }
            }
        }

        /// <summary>
        /// 关闭现有连接
        /// </summary>
        private void CloseExistingConnection()
        {
            try
            {
                _tcpClient?.Close();
                _tcpClient?.Dispose();
                _tcpClient = null;
            }
            catch { }
        }

        /// <summary>
        /// 启动心跳检测
        /// </summary>
        private void StartHeartbeat()
        {
            StopHeartbeat();

            if (!EnableHeartbeat) return;

            _heartbeatCts = new CancellationTokenSource();
            var token = _heartbeatCts.Token;
            _heartbeatTask = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested && !_isDisposed && !_userRequestedDisconnect)
                {
                    try
                    {
                        await Task.Delay(HeartbeatInterval, token).ConfigureAwait(false);
                        if (token.IsCancellationRequested || _isDisposed || _userRequestedDisconnect)
                            break;

                        bool alive = await HeartbeatToggleAsync(token).ConfigureAwait(false);
                        if (!alive)
                        {
                            OnConnectionLost();
                            return;
                        }
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"心跳检测异常: {ex.Message}");
                        OnConnectionLost();
                        return;
                    }
                }
            }, token);
        }

        /// <summary>
        /// 心跳读写：先读 HeartbeatAddress 当前值，再写入反转值，验证双向通信。
        /// 复用已有高层读写方法，自动处理字节序、异常、事务ID。
        /// </summary>
        private async Task<bool> HeartbeatToggleAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (!IsConnected || _isDisposed) return false;
                var fc = HeartbeatFunctionCode;
                bool readOk;
                object currentValue;
                if (fc == 0x01 || fc == 0x02)
                {
                    var r = await ReadBoolAsync(HeartbeatAddress, fc).ConfigureAwait(false);
                    readOk = r.IsSuccess;
                    currentValue = r.Value;
                }
                else
                {
                    var r = await ReadUInt16Async(HeartbeatAddress, fc).ConfigureAwait(false);
                    readOk = r.IsSuccess;
                    currentValue = r.Value;
                }

                if (!readOk) return false;

                if (fc == 0x01 || fc == 0x02)
                {
                    bool cur = currentValue is bool b && b;
                    return (await WriteBoolAsync(HeartbeatAddress, !cur).ConfigureAwait(false)).IsSuccess;
                }
                else
                {
                    ushort cur = currentValue is ushort u ? u : (ushort)0;
                    ushort inv = cur == 0 ? (ushort)1 : (ushort)0;
                    return (await WriteUInt16Async(HeartbeatAddress, inv).ConfigureAwait(false)).IsSuccess;
                }
            }
            catch (OperationCanceledException)
            {
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 异步读取完整的 Modbus TCP 响应帧。
        /// 先读取 6 字节 MBAP 头，再按 Length 字段读完剩余字节
        /// </summary>
        private async Task<int> ReadModbusResponseAsync(NetworkStream stream, CancellationToken token = default)
        {
            int totalRead = 0;
            while (totalRead < 6)
            {
                int n = await stream.ReadAsync(_readBuffer, totalRead, 6 - totalRead, token).ConfigureAwait(false);
                if (n == 0)
                {
                    token.ThrowIfCancellationRequested();
                    throw new IOException("连接已断开，读取 MBAP 头失败");
                }
                totalRead += n;
            }
            int remaining = (_readBuffer[4] << 8) | _readBuffer[5];
            if (remaining < 2 || remaining > 260)
                throw new IOException($"Modbus TCP 响应 Length 字段异常: {remaining}");
            while (totalRead < 6 + remaining)
            {
                int n = await stream.ReadAsync(_readBuffer, totalRead, 6 + remaining - totalRead, token).ConfigureAwait(false);
                if (n == 0)
                {
                    token.ThrowIfCancellationRequested();
                    throw new IOException("连接已断开，读取响应体失败");
                }
                totalRead += n;
            }
            return totalRead;
        }

        /// <summary>
        /// 同步读取完整的 Modbus TCP 响应帧。
        /// 先读取 6 字节 MBAP 头，再按 Length 字段读完剩余字节
        /// </summary>
        private int ReadModbusResponseSync(NetworkStream stream)
        {
            // 读取MBAP 
            int totalRead = 0;
            while (totalRead < 6)
            {
                int n = stream.Read(_readBuffer, totalRead, 6 - totalRead);
                if (n == 0) throw new IOException("连接已断开，读取 MBAP 头失败");
                totalRead += n;
            }
            int remaining = (_readBuffer[4] << 8) | _readBuffer[5];
            if (remaining < 2 || remaining > 260)
                throw new IOException($"Modbus TCP 响应 Length 字段异常: {remaining}");
            while (totalRead < 6 + remaining)
            {
                int n = stream.Read(_readBuffer, totalRead, 6 + remaining - totalRead);
                if (n == 0) throw new IOException("连接已断开，读取响应体失败");
                totalRead += n;
            }
            return totalRead;
        }

        /// <summary>
        /// 停止心跳检测
        /// </summary>
        private void StopHeartbeat()
        {
            try
            {
                _heartbeatCts?.Cancel();
                _heartbeatCts?.Dispose();
                _heartbeatCts = null;
            }
            catch { }
        }



        /// <summary>
        /// 连接丢失时的处理
        /// </summary>
        private void OnConnectionLost()
        {
            if (_userRequestedDisconnect || _isDisposed) return;

            if (Interlocked.CompareExchange(ref _connectionLostHandled, 1, 0) != 0)
                return;

            bool wasConnected = _lastKnownConnectedState;
            _lastKnownConnectedState = false;

            StopHeartbeat();

            var ex = new Exception($"与服务器 {TargetIP}:{TargetPort} 的连接已断开");
            if (wasConnected)
            {
                OnDisconnection(ex);
            }
            CloseExistingConnection();
            // 取消旧的重连令牌
            try { _reconnectCts?.Cancel(); } catch { }
            if (AutoReconnect && !_isDisposed && !_userRequestedDisconnect)
            {
                Task.Run(() => ConnectWithRetryAsync(notifyOnFirstFailure: false));
            }
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        public async Task DisconnectAsync()
        {
            _userRequestedDisconnect = true;
            StopHeartbeat();
            await DisconnectInternalAsync();
            _lastKnownConnectedState = false;
            OnDisconnection(new Exception("用户主动断开连接"));
        }

        /// <summary>
        /// 内部断开连接实现
        /// </summary>
        private Task DisconnectInternalAsync()
        {
            try
            {
                StopHeartbeat();
                CloseExistingConnection();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during disconnect: {ex.Message}");
            }

            return Task.CompletedTask;
        }

        #endregion

        #region 核心通信方法

        /// <summary>
        /// 异步发送并接收数据包
        /// </summary>
        private async Task<Package<T>> SendReceiveAsync<T>(Package<T> package)
        {
            ThrowIfDisposed();
            if (!IsConnected)
            {
                package.IsSuccess = false;
                package.ErrorMessage = "未连接到服务器";
                return package;
            }

            // 异步读取超时令牌
            CancellationTokenSource linkedCts = null;
            try
            {
                CancellationToken token = _cancellationTokenSource.Token;
                if (ReadTimeout > 0)
                {
                    linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token);
                    linkedCts.CancelAfter(ReadTimeout);
                    token = linkedCts.Token;
                }

                await _sendLock.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    var client = _tcpClient;
                    if (client == null || !client.Connected || _isDisposed)
                    {
                        package.IsSuccess = false;
                        package.ErrorMessage = "未连接到服务器";
                        return package;
                    }
                    var stream = client.GetStream();
                    var startTime = DateTime.Now;
                    package.ReceiveBuff = new byte[package.SendBuff.Length][];
                    var DataBuff = new byte[package.SendBuff.Length][];
                    bool[] success = new bool[package.SendBuff.Length];
                    for (int i = 0; i < package.SendBuff.Length; i++)
                    {
                        await stream.WriteAsync(package.SendBuff[i], 0, package.SendBuff[i].Length, token).ConfigureAwait(false);
                        int bytesRead = await ReadModbusResponseAsync(stream, token).ConfigureAwait(false);
                        package.ReceiveBuff[i] = new byte[bytesRead];
                        Array.Copy(_readBuffer, 0, package.ReceiveBuff[i], 0, bytesRead);
                        if (bytesRead >= 8)
                        {
                            if (_readBuffer[0] != package.SendBuff[i][0] ||
                                _readBuffer[1] != package.SendBuff[i][1] ||
                                (_readBuffer[7] != package.SendBuff[i][7] && _readBuffer[7] != (package.SendBuff[i][7] | 0x80)))
                            {
                                success[i] = false;
                            }
                            else
                            {
                                if ((_readBuffer[7] & 0x80) != 0)
                                {
                                    package.ExceptionCode = _readBuffer[8];
                                    package.ErrorMessage = ExceptionCode.GetDescription(_readBuffer[8]);
                                    success[i] = false;
                                }
                                else
                                {
                                    package.FunctionCode = _readBuffer[7];
                                    byte responseFc = _readBuffer[7];
                                    if (responseFc == 0x01 || responseFc == 0x02 || responseFc == 0x03 || responseFc == 0x04)
                                    {
                                        DataBuff[i] = new byte[package.ReceiveBuff[i][8]];
                                        Array.Copy(package.ReceiveBuff[i], 9, DataBuff[i], 0, DataBuff[i].Length);
                                    }
                                    else
                                    {
                                        DataBuff[i] = new byte[0];
                                    }
                                    success[i] = true;
                                }
                            }
                        }
                        else
                        {
                            success[i] = false;
                        }
                    }
                    int totalLength = 0;
                    foreach (var subArray in DataBuff)
                    {
                        if (subArray != null)
                            totalLength += subArray.Length;
                    }
                    package.DataBuff = new byte[totalLength];
                    int offset = 0;
                    foreach (var subArray in DataBuff)
                    {
                        if (subArray != null)
                        {
                            Buffer.BlockCopy(subArray, 0, package.DataBuff, offset, subArray.Length);
                            offset += subArray.Length;
                        }
                    }
                    package.IsSuccess = success.All(x => x);
                    package.ElapsedTime = DateTime.Now - startTime;
                }
                catch (OperationCanceledException) when (!_cancellationTokenSource.IsCancellationRequested)
                {
                    // ReadTimeout超时
                    package.IsSuccess = false;
                    package.ErrorMessage = $"读取超时({ReadTimeout}ms)";
                    OnConnectionLost();
                }
                catch (OperationCanceledException)
                {
                    // Dispose取消
                    package.IsSuccess = false;
                    package.ErrorMessage = "操作已取消";
                }
                catch (Exception ex)
                {
                    package.IsSuccess = false;
                    package.ErrorMessage = ex.Message;
                    if (ex is IOException || ex is SocketException || ex is TimeoutException)
                        OnConnectionLost();
                }
                finally
                {
                    _sendLock.Release();
                }
            }
            finally
            {
                linkedCts?.Dispose();
            }
            return package;
        }

        /// <summary>
        /// 同步发送并接收数据包
        /// </summary>
        private Package<T> SendReceiveSync<T>(Package<T> package)
        {
            ThrowIfDisposed();
            if (!IsConnected)
            {
                package.IsSuccess = false;
                package.ErrorMessage = "未连接到服务器";
                return package;
            }
            int lockTimeout = ReadTimeout > 0 ? ReadTimeout : Timeout.Infinite;
            if (!_sendLock.Wait(lockTimeout, _cancellationTokenSource.Token))
            {
                package.IsSuccess = false;
                package.ErrorMessage = $"等待信号量超时({ReadTimeout}ms)";
                return package;
            }
            try
            {
                var client = _tcpClient;
                if (client == null || !client.Connected || _isDisposed)
                {
                    package.IsSuccess = false;
                    package.ErrorMessage = "未连接到服务器";
                    return package;
                }
                return SendReceiveCore(client.GetStream(), package);
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
                if (ex is IOException || ex is SocketException || ex is TimeoutException)
                    OnConnectionLost();
            }
            finally
            {
                _sendLock.Release();
            }
            return package;
        }

        /// <summary>
        /// 同步收发核心逻辑
        /// </summary>
        private Package<T> SendReceiveCore<T>(NetworkStream stream, Package<T> package)
        {
            var startTime = DateTime.Now;
            package.ReceiveBuff = new byte[package.SendBuff.Length][];
            var DataBuff = new byte[package.SendBuff.Length][];
            bool[] success = new bool[package.SendBuff.Length];

            for (int i = 0; i < package.SendBuff.Length; i++)
            {
                stream.Write(package.SendBuff[i], 0, package.SendBuff[i].Length);
                int bytesRead = ReadModbusResponseSync(stream);
                package.ReceiveBuff[i] = new byte[bytesRead];
                Array.Copy(_readBuffer, 0, package.ReceiveBuff[i], 0, bytesRead);
                if (bytesRead >= 8)
                {
                    if (_readBuffer[0] != package.SendBuff[i][0] ||
                        _readBuffer[1] != package.SendBuff[i][1] ||
                        (_readBuffer[7] != package.SendBuff[i][7] && _readBuffer[7] != (package.SendBuff[i][7] | 0x80)))
                    {
                        success[i] = false;
                    }
                    else
                    {
                        if ((_readBuffer[7] & 0x80) != 0)
                        {
                            package.ExceptionCode = _readBuffer[8];
                            package.ErrorMessage = ExceptionCode.GetDescription(_readBuffer[8]);
                            success[i] = false;
                        }
                        else
                        {
                            package.FunctionCode = _readBuffer[7];
                            byte responseFc = _readBuffer[7];
                            if (responseFc == 0x01 || responseFc == 0x02 || responseFc == 0x03 || responseFc == 0x04)
                            {
                                int dataLen = package.ReceiveBuff[i][8];
                                if (9 + dataLen > package.ReceiveBuff[i].Length)
                                {
                                    success[i] = false;
                                }
                                else
                                {
                                    DataBuff[i] = new byte[dataLen];
                                    Array.Copy(package.ReceiveBuff[i], 9, DataBuff[i], 0, dataLen);
                                    success[i] = true;
                                }
                            }
                            else
                            {
                                DataBuff[i] = new byte[0];
                                success[i] = true;
                            }
                        }
                    }
                }
                else
                {
                    success[i] = false;
                }
            }
            int totalLength = 0;
            foreach (var subArray in DataBuff)
            {
                if (subArray != null)
                    totalLength += subArray.Length;
            }
            package.DataBuff = new byte[totalLength];
            int offset = 0;
            foreach (var subArray in DataBuff)
            {
                if (subArray != null)
                {
                    Buffer.BlockCopy(subArray, 0, package.DataBuff, offset, subArray.Length);
                    offset += subArray.Length;
                }
            }
            package.IsSuccess = success.All(x => x);
            package.ElapsedTime = DateTime.Now - startTime;
            return package;
        }

        /// <summary>
        /// 验证响应数据
        /// </summary>
        private static bool ValidateResponse(byte[] request, byte[] response)
        {
            if (request == null || response == null || response.Length < 8)
                return false;
            if (response[0] != request[0] || response[1] != request[1])
                return false;
            if ((response[7] & 0x80) != 0)
            {
                return false;
            }
            return response[7] == request[7];
        }

        /// <summary>
        /// 检查响应是否为异常
        /// </summary>
        private static bool IsExceptionResponse(byte[] response, out byte exceptionCode, out string errorMessage)
        {
            exceptionCode = 0;
            errorMessage = string.Empty;
            if (response == null || response.Length < 9)
                return false;
            if ((response[7] & 0x80) != 0)
            {
                exceptionCode = response[8];
                errorMessage = ExceptionCode.GetDescription(exceptionCode);
                return true;
            }
            return false;
        }

        #endregion

        #region 消息构建方法

        /// <summary>
        /// 生成下一个事务ID
        /// </summary>
        private int GetNextTransactionId()
        {
            return Interlocked.Increment(ref _transactionId) & 0xFFFF;
        }

        /// <summary>
        /// 构建读取消息
        /// </summary>
        private byte[] BuildReadMessage(byte function, string address, int length)
        {
            if (length > MAX_REGISTERS_PER_READ)
                throw new ArgumentException($"单次读取寄存器数量不能超过 {MAX_REGISTERS_PER_READ}");
            ushort addr = NormalizeAddress(address);
            var message = new byte[12];
            int transactionId = GetNextTransactionId();

            message[0] = (byte)(transactionId >> 8);
            message[1] = (byte)(transactionId & 0xFF);
            message[2] = 0; // Protocol ID 高字节
            message[3] = 0; // Protocol ID 低字节
            message[4] = 0; // Length 高字节
            message[5] = 6; // Length 低字节
            message[6] = (byte)StationNumber;
            message[7] = function;
            message[8] = (byte)(addr >> 8);
            message[9] = (byte)(addr & 0xFF);
            message[10] = (byte)(length >> 8);
            message[11] = (byte)(length & 0xFF);

            return message;
        }

        /// <summary>
        /// 写入单个寄存器消息 (功能码 0x06)
        /// </summary>
        private byte[] BuildWriteSingleRegisterMessage(string address, ushort value)
        {
            ushort addr = NormalizeAddress(address);

            var message = new byte[12];
            int transactionId = GetNextTransactionId();
            message[0] = (byte)(transactionId >> 8);
            message[1] = (byte)(transactionId & 0xFF);
            message[2] = 0; // Protocol ID 高字节
            message[3] = 0; // Protocol ID 低字节
            message[4] = 0; // Length 高字节
            message[5] = 6; // Length 低字节 (单元标识符 + 功能码 + 地址 + 值 = 1+1+2+2=6)
            message[6] = (byte)StationNumber;
            message[7] = 0x06; // 写入单个寄存器功能码
            message[8] = (byte)(addr >> 8);
            message[9] = (byte)(addr & 0xFF);
            message[10] = (byte)(value >> 8);
            message[11] = (byte)(value & 0xFF);

            return message;
        }

        /// <summary>
        /// 写入多个寄存器消息 (功能码 0x10)
        /// </summary>
        private byte[] BuildWriteMultipleRegistersMessage(string address, byte[] data)
        {
            ushort addr = NormalizeAddress(address);

            if (data.Length % 2 != 0)
                throw new ArgumentException("数据长度必须为偶数", nameof(data));

            int registerCount = data.Length / 2;
            int pduLength = 7 + data.Length; // 单元标识符(1) + 功能码(1) + 地址(2) + 寄存器数量(2) + 字节数(1) + 数据
            var message = new byte[6 + pduLength];
            int transactionId = GetNextTransactionId();
            message[0] = (byte)(transactionId >> 8);
            message[1] = (byte)(transactionId & 0xFF);
            message[2] = 0; // Protocol ID 高字节
            message[3] = 0; // Protocol ID 低字节
            message[4] = (byte)(pduLength >> 8); // Length 高字节
            message[5] = (byte)(pduLength & 0xFF); // Length 低字节
            message[6] = (byte)StationNumber;
            message[7] = 0x10; // 写入多个寄存器功能码
            message[8] = (byte)(addr >> 8);
            message[9] = (byte)(addr & 0xFF);
            message[10] = (byte)(registerCount >> 8);
            message[11] = (byte)(registerCount & 0xFF);
            message[12] = (byte)data.Length; // 字节数

            Array.Copy(data, 0, message, 13, data.Length);
            return message;
        }

        /// <summary>
        /// 写入单个线圈消息 (功能码 0x05)
        /// </summary>
        private byte[] BuildWriteSingleCoilMessage(string address, bool value)
        {
            ushort addr = NormalizeAddress(address);
            var message = new byte[12];
            int transactionId = GetNextTransactionId();
            message[0] = (byte)(transactionId >> 8);
            message[1] = (byte)(transactionId & 0xFF);
            message[2] = 0; // Protocol ID 高字节
            message[3] = 0; // Protocol ID 低字节
            message[4] = 0; // Length 高字节
            message[5] = 6; // Length 低字节
            message[6] = (byte)StationNumber;
            message[7] = 0x05; // 写入单个线圈功能码
            message[8] = (byte)(addr >> 8);
            message[9] = (byte)(addr & 0xFF);
            message[10] = value ? (byte)0xFF : (byte)0x00;
            message[11] = 0x00;

            return message;
        }

        /// <summary>
        /// 写入多个线圈消息 (功能码 0x0F)
        /// </summary>
        private byte[] BuildWriteMultipleCoilsMessage(string address, bool[] values)
        {
            ushort addr = NormalizeAddress(address);
            int byteCount = (values.Length + 7) / 8;
            byte[] coilBytes = new byte[byteCount];
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i])
                    coilBytes[i / 8] |= (byte)(1 << (i % 8));
            }
            int pduLength = 7 + byteCount; // 单元标识符(1) + 功能码(1) + 地址(2) + 线圈数量(2) + 字节数(1) + 数据
            var message = new byte[6 + pduLength];
            int transactionId = GetNextTransactionId();
            message[0] = (byte)(transactionId >> 8);
            message[1] = (byte)(transactionId & 0xFF);
            message[2] = 0; // Protocol ID 高字节
            message[3] = 0; // Protocol ID 低字节
            message[4] = (byte)(pduLength >> 8); // Length 高字节
            message[5] = (byte)(pduLength & 0xFF); // Length 低字节
            message[6] = (byte)StationNumber;
            message[7] = 0x0F; // 写入多个线圈功能码
            message[8] = (byte)(addr >> 8);
            message[9] = (byte)(addr & 0xFF);
            message[10] = (byte)(values.Length >> 8);
            message[11] = (byte)(values.Length & 0xFF);
            message[12] = (byte)byteCount;
            Array.Copy(coilBytes, 0, message, 13, byteCount);

            return message;
        }

        /// <summary>
        /// 多个读取消息（用于长度超过125的读取）
        /// </summary>
        private byte[][] BuildReadMessages(byte function, string address, int length)
        {
            if (!int.TryParse(address, out int startAddress))
                throw new ArgumentException("地址格式错误", nameof(address));
            var messages = new List<byte[]>();
            int remainingLength = length;
            int currentAddress = startAddress;
            while (remainingLength > 0)
            {
                int currentLength = Math.Min(remainingLength, MAX_REGISTERS_PER_READ);
                messages.Add(BuildReadMessage(function, currentAddress.ToString(), currentLength));
                currentAddress += currentLength;
                remainingLength -= currentLength;
            }
            return messages.ToArray();
        }

        /// <summary>
        /// 统一地址解析与基址偏移
        /// </summary>
        private ushort NormalizeAddress(string address)
        {
            if (!int.TryParse(address, out int addr))
                throw new ArgumentException("地址格式错误", nameof(address));
            if (AddressStartsAtOne)
            {
                if (addr <= 0)
                    throw new ArgumentOutOfRangeException(nameof(address), "地址必须大于0（1基址）");
                addr -= 1;
            }
            if (addr < 0 || addr > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(address), "地址超出范围");
            return (ushort)addr;
        }

        #endregion

        #region 字节序转换

        /// <summary>
        /// 批量转换字节序
        /// </summary>
        public static byte[] ConvertByteOrderBatch(byte[] bytes, int wordLength, ByteOrder byteOrder)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));

            if (wordLength <= 0 || bytes.Length % wordLength != 0)
                throw new ArgumentException("字节数组长度必须为字长的整数倍", nameof(wordLength));

            var result = new byte[bytes.Length];
            int groupCount = bytes.Length / wordLength;
            for (int group = 0; group < groupCount; group++)
            {
                int offset = group * wordLength;
                ConvertSingleWordByteOrder(bytes, result, offset, wordLength, byteOrder);
            }
            return result;
        }

        /// <summary>
        /// 转换单个字的字节序
        /// <para>ABCD（大端序）：线序 [01,02,03,04] — 高字在前，字内高字节在前</para>
        /// <para>BADC（字节交换）：线序 [02,01,04,03] — 高字在前，字内低字节在前</para>
        /// <para>CDAB（字交换序）：线序 [03,04,01,02] — 低字在前，字内高字节在前</para>
        /// <para>DCBA（小端序）：线序 [04,03,02,01] — 低字在前，字内低字节在前</para>
        /// </summary>
        private static void ConvertSingleWordByteOrder(byte[] source, byte[] destination, int offset, int wordLength, ByteOrder byteOrder)
        {
            switch (byteOrder)
            {
                case ByteOrder.ABCD:
                    for (int i = 0; i < wordLength; i++)
                    {
                        destination[offset + i] = source[offset + wordLength - 1 - i];
                    }
                    break;

                case ByteOrder.BADC:
                    if (wordLength < 2 || wordLength % 2 != 0)
                        throw new ArgumentException($"BADC字节序不支持{wordLength}字节长度", nameof(wordLength));
                    for (int w = 0; w < wordLength; w += 2)
                    {
                        int srcWord = wordLength - 2 - w;
                        destination[offset + w] = source[offset + srcWord];
                        destination[offset + w + 1] = source[offset + srcWord + 1];
                    }
                    break;

                case ByteOrder.CDAB:
                    if (wordLength < 2 || wordLength % 2 != 0)
                        throw new ArgumentException($"CDAB字节序不支持{wordLength}字节长度", nameof(wordLength));
                    for (int w = 0; w < wordLength; w += 2)
                    {
                        destination[offset + w] = source[offset + w + 1];
                        destination[offset + w + 1] = source[offset + w];
                    }
                    break;

                case ByteOrder.DCBA:
                    Array.Copy(source, offset, destination, offset, wordLength);
                    break;

                default:
                    throw new ArgumentException("不支持的字节序", nameof(byteOrder));
            }
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 字节转换为位数组
        /// </summary>
        private static bool[] BytesToBitsReversed(byte[] bytes, int maxBits)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));

            var bits = new bool[maxBits];
            int bitCount = 0;

            for (int i = 0; i < bytes.Length && bitCount < maxBits; i++)
            {
                byte b = bytes[i];
                for (int j = 0; j < 8 && bitCount < maxBits; j++)
                {
                    bits[bitCount++] = (b & (1 << j)) != 0;
                }
            }

            return bits;
        }

        /// <summary>
        /// 触发交互事件
        /// </summary>
        private void OnInteractionEvent(TimeSpan timeSpan, bool result, string operation, string address, int registerCount, string operationValue)
        {
            try
            {
                InteractionEvent?.Invoke(timeSpan, result, operation, address, registerCount, operationValue);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"InteractionEvent error: {ex.Message}");
            }
        }

        /// <summary>
        /// 检查是否已释放
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(ModbusTCPClientPlus));
        }

        /// <summary>
        /// 触发断开连接事件
        /// </summary>
        private void OnDisconnection(Exception ex)
        {
            try
            {
                DisconnectionEvent?.Invoke(DateTime.Now, TargetIP ?? string.Empty, TargetPort, ex);
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine($"DisconnectionEvent error: {e.Message}");
            }
        }

        /// <summary>
        /// 触发连接成功事件
        /// </summary>
        private void OnSuccessfulConnect()
        {
            try
            {
                SuccessfulConnectEvent?.Invoke(DateTime.Now, TargetIP ?? string.Empty, TargetPort);
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine($"SuccessfulConnectEvent error: {e.Message}");
            }
        }

        #endregion

        #region 读取方法

        private async Task<Package<bool>> ReadBitASync(string address, byte function = 0x03)
        {
            var package = new Package<bool> { Address = address };
            string result = "Null";
            try
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("功能码必须是 0x03 或 0x04", nameof(function));
                if (string.IsNullOrWhiteSpace(address))
                    throw new ArgumentException("地址不能为空", nameof(address));
                string[] parts = address.Split('.');
                if (parts.Length != 2)
                    throw new FormatException("地址格式必须为 寄存器.位 例如 100.2");
                if (!int.TryParse(parts[0], out int registerAddress))
                    throw new FormatException("寄存器地址格式错误");
                if (!int.TryParse(parts[1], out int bitIndex))
                    throw new FormatException("位索引格式错误");
                if (bitIndex < 0 || bitIndex > 15)
                    throw new ArgumentOutOfRangeException(nameof(address), "bitIndex 必须在 0~15 之间");
                package.SendBuff = BuildReadMessages(function, registerAddress.ToString(), 1);
                package = await SendReceiveAsync(package);
                if (!package.IsSuccess)
                    return package;
                if (package.DataBuff.Length != 2)
                    throw new Exception("返回数据长度异常");
                var bytes = ConvertByteOrderBatch(package.DataBuff, 2, Order);
                ushort registerValue = BitConverter.ToUInt16(bytes, 0);
                bool bitValue = (registerValue & (1 << bitIndex)) != 0;
                package.Value = bitValue;
                package.IsSuccess = true;
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
                if (ex is IOException || ex is SocketException || ex is TimeoutException)
                    OnDisconnection(ex);
            }
            OnInteractionEvent(package.ElapsedTime,
                               package.IsSuccess,
                               "ReadSingleBit",
                               address,
                               1,
                               result);
            return package;
        }

        /// <summary>
        /// 异步读取布尔值（线圈/离散输入）
        /// </summary>
        /// <param name="address">起始地址</param>
        /// <param name="function">功能码：0x01=读线圈，0x02=读离散输入</param>
        public async Task<Package<bool>> ReadBoolAsync(string address, byte function = 0x01)
        {
            var package = new Package<bool> { Address = address };
            try
            {
                if (function != 0x01 && function != 0x02)
                    throw new ArgumentException("功能码必须是 0x01 或 0x02", nameof(function));
                package.SendBuff = BuildReadMessages(function, address, 1);
                package = await SendReceiveAsync(package);
                if (package.IsSuccess && package.DataBuff.Length > 0)
                {
                    package.Value = (package.DataBuff[0] & 0x01) != 0;
                }
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
                if (ex is IOException || ex is SocketException || ex is TimeoutException)
                    OnDisconnection(ex);
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "ReadBool", address, 1, package.Value.ToString());
            return package;
        }

        /// <summary>
        /// 异步读取多个布尔值（线圈/离散输入）
        /// </summary>
        public async Task<Package<bool[]>> ReadBoolArrayAsync(string address, int count, byte function = 0x01)
        {
            var package = new Package<bool[]> { Address = address };
            try
            {
                if (function != 0x01 && function != 0x02)
                    throw new ArgumentException("功能码必须是 0x01 或 0x02", nameof(function));
                package.SendBuff = BuildReadMessages(function, address, count);
                package = await SendReceiveAsync(package);
                if (package.IsSuccess && package.DataBuff.Length > 0)
                {
                    package.Value = BytesToBitsReversed(package.DataBuff, count);
                }
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
                if (ex is IOException || ex is SocketException || ex is TimeoutException)
                    OnDisconnection(ex);
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "ReadBoolArray", address, count, package.Value?.Length.ToString() ?? "0");
            return package;
        }

        /// <summary>
        /// 异步读取16位有符号整数
        /// </summary>
        public async Task<Package<short>> ReadInt16Async(string address, byte function = 0x03)
        {
            var package = new Package<short> { Address = address };
            try
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("功能码必须是 0x03 或 0x04", nameof(function));
                package.SendBuff = BuildReadMessages(function, address, 1);
                package = await SendReceiveAsync(package);
                if (package.IsSuccess && package.DataBuff.Length >= 2)
                {
                    var convertedBytes = ConvertByteOrderBatch(package.DataBuff, 2, Order);
                    package.Value = BitConverter.ToInt16(convertedBytes, 0);
                }
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
                if (ex is IOException || ex is SocketException || ex is TimeoutException)
                    OnDisconnection(ex);
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "ReadInt16", address, 1, package.Value.ToString());
            return package;
        }

        /// <summary>
        /// 异步读取16位无符号整数
        /// </summary>
        public async Task<Package<ushort>> ReadUInt16Async(string address, byte function = 0x03)
        {
            var package = new Package<ushort> { Address = address };
            try
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("功能码必须是 0x03 或 0x04", nameof(function));
                package.SendBuff = BuildReadMessages(function, address, 1);
                package = await SendReceiveAsync(package);
                if (package.IsSuccess && package.DataBuff.Length >= 2)
                {
                    var convertedBytes = ConvertByteOrderBatch(package.DataBuff, 2, Order);
                    package.Value = BitConverter.ToUInt16(convertedBytes, 0);
                }
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
                if (ex is IOException || ex is SocketException || ex is TimeoutException)
                    OnDisconnection(ex);
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "ReadUInt16", address, 1, package.Value.ToString());
            return package;
        }

        /// <summary>
        /// 异步读取32位有符号整数
        /// </summary>
        public async Task<Package<int>> ReadInt32Async(string address, byte function = 0x03)
        {
            var package = new Package<int> { Address = address };
            try
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("功能码必须是 0x03 或 0x04", nameof(function));
                package.SendBuff = BuildReadMessages(function, address, 2);
                package = await SendReceiveAsync(package);
                if (package.IsSuccess && package.DataBuff.Length >= 4)
                {
                    var convertedBytes = ConvertByteOrderBatch(package.DataBuff, 4, Order);
                    package.Value = BitConverter.ToInt32(convertedBytes, 0);
                }
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
                if (ex is IOException || ex is SocketException || ex is TimeoutException)
                    OnDisconnection(ex);
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "ReadInt32", address, 2, package.Value.ToString());
            return package;
        }

        /// <summary>
        /// 异步读取32位无符号整数
        /// </summary>
        public async Task<Package<uint>> ReadUInt32Async(string address, byte function = 0x03)
        {
            var package = new Package<uint> { Address = address };
            try
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("功能码必须是 0x03 或 0x04", nameof(function));

                package.SendBuff = BuildReadMessages(function, address, 2);
                package = await SendReceiveAsync(package);
                if (package.IsSuccess && package.DataBuff.Length >= 4)
                {
                    var convertedBytes = ConvertByteOrderBatch(package.DataBuff, 4, Order);
                    package.Value = BitConverter.ToUInt32(convertedBytes, 0);
                }
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
                if (ex is IOException || ex is SocketException || ex is TimeoutException)
                    OnDisconnection(ex);
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "ReadUInt32", address, 2, package.Value.ToString());
            return package;
        }

        /// <summary>
        /// 异步读取64位有符号整数
        /// </summary>
        public async Task<Package<long>> ReadInt64Async(string address, byte function = 0x03)
        {
            var package = new Package<long> { Address = address };
            try
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("功能码必须是 0x03 或 0x04", nameof(function));
                package.SendBuff = BuildReadMessages(function, address, 4);
                package = await SendReceiveAsync(package);
                if (package.IsSuccess && package.DataBuff.Length >= 8)
                {
                    var convertedBytes = ConvertByteOrderBatch(package.DataBuff, 8, Order);
                    package.Value = BitConverter.ToInt64(convertedBytes, 0);
                }
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
                if (ex is IOException || ex is SocketException || ex is TimeoutException)
                    OnDisconnection(ex);
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "ReadInt64", address, 4, package.Value.ToString());
            return package;
        }

        /// <summary>
        /// 异步读取32位浮点数
        /// </summary>
        public async Task<Package<float>> ReadFloatAsync(string address, byte function = 0x03)
        {
            var package = new Package<float> { Address = address };
            try
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("功能码必须是 0x03 或 0x04", nameof(function));
                package.SendBuff = BuildReadMessages(function, address, 2);
                package = await SendReceiveAsync(package);
                if (package.IsSuccess && package.DataBuff.Length >= 4)
                {
                    var convertedBytes = ConvertByteOrderBatch(package.DataBuff, 4, Order);
                    package.Value = BitConverter.ToSingle(convertedBytes, 0);
                }
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
                if (ex is IOException || ex is SocketException || ex is TimeoutException)
                    OnDisconnection(ex);
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "ReadFloat", address, 2, package.Value.ToString("F4"));
            return package;
        }

        /// <summary>
        /// 异步读取64位双精度浮点数
        /// </summary>
        public async Task<Package<double>> ReadDoubleAsync(string address, byte function = 0x03)
        {
            var package = new Package<double> { Address = address };
            try
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("功能码必须是 0x03 或 0x04", nameof(function));

                package.SendBuff = BuildReadMessages(function, address, 4);
                package = await SendReceiveAsync(package);

                if (package.IsSuccess && package.DataBuff.Length >= 8)
                {
                    var convertedBytes = ConvertByteOrderBatch(package.DataBuff, 8, Order);
                    package.Value = BitConverter.ToDouble(convertedBytes, 0);
                }
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
                if (ex is IOException || ex is SocketException || ex is TimeoutException)
                    OnDisconnection(ex);
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "ReadDouble", address, 4, package.Value.ToString("F6"));
            return package;
        }

        /// <summary>
        /// 异步读取字符串（ASCII编码）
        /// </summary>
        /// <param name="address">起始地址</param>
        /// <param name="length">字符串长度（字节数）</param>
        /// <param name="function">功能码</param>
        public async Task<Package<string>> ReadStringAsync(string address, int length, byte function = 0x03)
        {
            var package = new Package<string> { Address = address };
            try
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("功能码必须是 0x03 或 0x04", nameof(function));
                if (length <= 0)
                    throw new ArgumentException("字符串读取长度必须大于0", nameof(function));
                int registerCount = (length + 1) / 2;
                package.SendBuff = BuildReadMessages(function, address, registerCount);
                package = await SendReceiveAsync(package);
                if (package.IsSuccess && package.DataBuff.Length > 0)
                {
                    var stringBytes = new byte[Math.Min(package.DataBuff.Length, length)];
                    switch (Order)
                    {
                        case ByteOrder.ABCD:
                        case ByteOrder.CDAB:
                            Array.Copy(package.DataBuff, stringBytes, stringBytes.Length);
                            break;

                        case ByteOrder.BADC:
                        case ByteOrder.DCBA:
                            for (int i = 0; i < stringBytes.Length; i += 2)
                            {
                                if (i + 1 < package.DataBuff.Length)
                                {
                                    stringBytes[i] = package.DataBuff[i + 1];
                                    if (i + 1 < stringBytes.Length)
                                        stringBytes[i + 1] = package.DataBuff[i];
                                }
                                else if (i < package.DataBuff.Length)
                                {
                                    stringBytes[i] = package.DataBuff[i];
                                }
                            }
                            break;
                    }
                    package.Value = Encoding.ASCII.GetString(stringBytes).TrimEnd('\0');
                }
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
                if (ex is IOException || ex is SocketException || ex is TimeoutException)
                    OnDisconnection(ex);
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "ReadString", address, length, package.Value ?? string.Empty);
            return package;
        }

        private async Task<Package<bool[]>> ReadBitArrayASync(string address, int registerLength, byte function = 0x03)
        {
            var package = new Package<bool[]> { Address = address };
            string result = "Null";
            try
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("功能码必须是 0x03 或 0x04", nameof(function));
                if (registerLength <= 0)
                    throw new ArgumentException("registerLength 必须大于 0", nameof(registerLength));
                package.SendBuff = BuildReadMessages(function, address, registerLength);
                package = await SendReceiveAsync(package);
                if (package.IsSuccess && package.DataBuff.Length >= registerLength * 2)
                {
                    var bytes = ConvertByteOrderBatch(package.DataBuff, 2, Order);
                    int totalBits = registerLength * 16;
                    bool[] bitStates = new bool[totalBits];
                    for (int i = 0; i < registerLength; i++)
                    {
                        ushort currentRegister = BitConverter.ToUInt16(bytes, i * 2);
                        for (int j = 0; j < 16; j++)
                        {
                            int bitIndex = i * 16 + j;
                            bitStates[bitIndex] = ((currentRegister & (1 << j)) != 0 ? true : false);
                        }
                    }
                    package.Value = bitStates;
                    result = string.Join(", ",
                        bitStates.Select((v, i) => $"{i}={v}"));
                }
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
                if (ex is IOException || ex is SocketException || ex is TimeoutException)
                    OnDisconnection(ex);
            }
            OnInteractionEvent(package.ElapsedTime,
                               package.IsSuccess,
                               "ReadBits",
                               address,
                               registerLength,
                               result);
            return package;
        }

        /// <summary>
        /// 异步读取多个16位整数
        /// </summary>
        public async Task<Package<short[]>> ReadInt16ArrayAsync(string address, int count, byte function = 0x03)
        {
            var package = new Package<short[]> { Address = address };
            try
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("功能码必须是 0x03 或 0x04", nameof(function));
                package.SendBuff = BuildReadMessages(function, address, count);
                package = await SendReceiveAsync(package);
                if (package.IsSuccess && package.DataBuff.Length >= count * 2)
                {
                    var values = new short[count];
                    for (int i = 0; i < count; i++)
                    {
                        var bytes = new byte[2];
                        Array.Copy(package.DataBuff, i * 2, bytes, 0, 2);
                        var convertedBytes = ConvertByteOrderBatch(bytes, 2, Order);
                        values[i] = BitConverter.ToInt16(convertedBytes, 0);
                    }
                    package.Value = values;
                }
                else if (package.IsSuccess)
                {
                    package.IsSuccess = false;
                    package.ErrorMessage = $"数据长度不足，期望 {count * 2} 字节，实际 {package.DataBuff?.Length ?? 0} 字节";
                }
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
                if (ex is IOException || ex is SocketException || ex is TimeoutException)
                    OnDisconnection(ex);
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "ReadInt16Array", address, count, package.Value?.Length.ToString() ?? "0");
            return package;
        }

        /// <summary>
        /// 异步读取多个16位无符号整数
        /// </summary>
        public async Task<Package<ushort[]>> ReadUInt16ArrayAsync(string address, int count, byte function = 0x03)
        {
            var package = new Package<ushort[]> { Address = address };
            try
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("功能码必须是 0x03 或 0x04", nameof(function));
                package.SendBuff = BuildReadMessages(function, address, count);
                package = await SendReceiveAsync(package);
                if (package.IsSuccess && package.DataBuff.Length >= count * 2)
                {
                    var values = new ushort[count];
                    for (int i = 0; i < count; i++)
                    {
                        var bytes = new byte[2];
                        Array.Copy(package.DataBuff, i * 2, bytes, 0, 2);
                        var convertedBytes = ConvertByteOrderBatch(bytes, 2, Order);
                        values[i] = BitConverter.ToUInt16(convertedBytes, 0);
                    }
                    package.Value = values;
                }
                else if (package.IsSuccess)
                {
                    package.IsSuccess = false;
                    package.ErrorMessage = $"数据长度不足，期望 {count * 2} 字节，实际 {package.DataBuff?.Length ?? 0} 字节";
                }
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
                if (ex is IOException || ex is SocketException || ex is TimeoutException)
                    OnDisconnection(ex);
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "ReadUInt16Array", address, count, package.Value?.Length.ToString() ?? "0");
            return package;
        }

        /// <summary>
        /// 异步读取多个32位有符号整数
        /// </summary>
        public async Task<Package<int[]>> ReadInt32ArrayAsync(string address, int count, byte function = 0x03)
        {
            var package = new Package<int[]> { Address = address };
            try
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("功能码必须是 0x03 或 0x04", nameof(function));

                package.SendBuff = BuildReadMessages(function, address, count * 2);
                package = await SendReceiveAsync(package);
                if (package.IsSuccess && package.DataBuff.Length >= count * 4)
                {
                    var values = new int[count];
                    for (int i = 0; i < count; i++)
                    {
                        var bytes = new byte[4];
                        Array.Copy(package.DataBuff, i * 4, bytes, 0, 4);
                        var convertedBytes = ConvertByteOrderBatch(bytes, 4, Order);
                        values[i] = BitConverter.ToInt32(convertedBytes, 0);
                    }
                    package.Value = values;
                }
                else if (package.IsSuccess)
                {
                    package.IsSuccess = false;
                    package.ErrorMessage = $"数据长度不足，期望 {count * 4} 字节，实际 {package.DataBuff?.Length ?? 0} 字节";
                }
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
                if (ex is IOException || ex is SocketException || ex is TimeoutException)
                    OnDisconnection(ex);
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "ReadInt32Array", address, count * 2, package.Value?.Length.ToString() ?? "0");
            return package;
        }

        /// <summary>
        /// 异步读取多个32位无符号整数
        /// </summary>
        public async Task<Package<uint[]>> ReadUInt32ArrayAsync(string address, int count, byte function = 0x03)
        {
            var package = new Package<uint[]> { Address = address };
            try
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("功能码必须是 0x03 或 0x04", nameof(function));

                package.SendBuff = BuildReadMessages(function, address, count * 2);
                package = await SendReceiveAsync(package);
                if (package.IsSuccess && package.DataBuff.Length >= count * 4)
                {
                    var values = new uint[count];
                    for (int i = 0; i < count; i++)
                    {
                        var bytes = new byte[4];
                        Array.Copy(package.DataBuff, i * 4, bytes, 0, 4);
                        var convertedBytes = ConvertByteOrderBatch(bytes, 4, Order);
                        values[i] = BitConverter.ToUInt32(convertedBytes, 0);
                    }
                    package.Value = values;
                }
                else if (package.IsSuccess)
                {
                    package.IsSuccess = false;
                    package.ErrorMessage = $"数据长度不足，期望 {count * 4} 字节，实际 {package.DataBuff?.Length ?? 0} 字节";
                }
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
                if (ex is IOException || ex is SocketException || ex is TimeoutException)
                    OnDisconnection(ex);
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "ReadUInt32Array", address, count * 2, package.Value?.Length.ToString() ?? "0");
            return package;
        }

        /// <summary>
        /// 异步读取多个浮点数
        /// </summary>
        public async Task<Package<float[]>> ReadFloatArrayAsync(string address, int count, byte function = 0x03)
        {
            var package = new Package<float[]> { Address = address };
            try
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("功能码必须是 0x03 或 0x04", nameof(function));
                package.SendBuff = BuildReadMessages(function, address, count * 2);
                package = await SendReceiveAsync(package);
                if (package.IsSuccess && package.DataBuff.Length >= count * 4)
                {
                    var values = new float[count];
                    for (int i = 0; i < count; i++)
                    {
                        var bytes = new byte[4];
                        Array.Copy(package.DataBuff, i * 4, bytes, 0, 4);
                        var convertedBytes = ConvertByteOrderBatch(bytes, 4, Order);
                        values[i] = BitConverter.ToSingle(convertedBytes, 0);
                    }
                    package.Value = values;
                }
                else if (package.IsSuccess)
                {
                    package.IsSuccess = false;
                    package.ErrorMessage = $"数据长度不足，期望 {count * 4} 字节，实际 {package.DataBuff?.Length ?? 0} 字节";
                }
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
                if (ex is IOException || ex is SocketException || ex is TimeoutException)
                    OnDisconnection(ex);
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "ReadFloatArray", address, count * 2, package.Value?.Length.ToString() ?? "0");
            return package;
        }

        /// <summary>
        /// 异步读取多个双精度浮点数
        /// </summary>
        public async Task<Package<double[]>> ReadDoubleArrayAsync(string address, int count, byte function = 0x03)
        {
            var package = new Package<double[]> { Address = address };
            try
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("功能码必须是 0x03 或 0x04", nameof(function));

                package.SendBuff = BuildReadMessages(function, address, count * 4);
                package = await SendReceiveAsync(package);

                if (package.IsSuccess && package.DataBuff.Length >= count * 8)
                {
                    var values = new double[count];
                    for (int i = 0; i < count; i++)
                    {
                        var bytes = new byte[8];
                        Array.Copy(package.DataBuff, i * 8, bytes, 0, 8);
                        var convertedBytes = ConvertByteOrderBatch(bytes, 8, Order);
                        values[i] = BitConverter.ToDouble(convertedBytes, 0);
                    }
                    package.Value = values;
                }
                else if (package.IsSuccess)
                {
                    package.IsSuccess = false;
                    package.ErrorMessage = $"数据长度不足，期望 {count * 8} 字节，实际 {package.DataBuff?.Length ?? 0} 字节";
                }
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
                if (ex is IOException || ex is SocketException || ex is TimeoutException)
                    OnDisconnection(ex);
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "ReadDoubleArray", address, count * 4, package.Value?.Length.ToString() ?? "0");
            return package;
        }

        #endregion

        #region 写入方法

        /// <summary>
        /// 异步写入单个线圈 (功能码 0x05)
        /// </summary>
        public async Task<Package<bool>> WriteBoolAsync(string address, bool value)
        {
            var package = new Package<bool> { Address = address, Value = value };
            try
            {
                package.SendBuff = new[] { BuildWriteSingleCoilMessage(address, value) };
                package = await SendReceiveAsync(package);
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
                if (ex is IOException || ex is SocketException || ex is TimeoutException)
                    OnDisconnection(ex);
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "WriteBool", address, 1, value.ToString());
            return package;
        }

        /// <summary>
        /// 异步写入多个线圈 (功能码 0x0F)
        /// </summary>
        public async Task<Package<bool[]>> WriteBoolArrayAsync(string address, bool[] values)
        {
            var package = new Package<bool[]> { Address = address, Value = values };
            try
            {
                package.SendBuff = new[] { BuildWriteMultipleCoilsMessage(address, values) };
                package = await SendReceiveAsync(package);
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
                if (ex is IOException || ex is SocketException || ex is TimeoutException)
                    OnDisconnection(ex);
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "WriteBoolArray", address, values.Length, values.Length.ToString());
            return package;
        }

        /// <summary>
        /// 异步写入16位有符号整数 (功能码 0x06)
        /// </summary>
        public async Task<Package<short>> WriteInt16Async(string address, short value)
        {
            var package = new Package<short> { Address = address, Value = value };
            try
            {
                package.SendBuff = new[] { BuildWriteSingleRegisterMessage(address, (ushort)value) };
                package = await SendReceiveAsync(package);
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
                if (ex is IOException || ex is SocketException || ex is TimeoutException)
                    OnDisconnection(ex);
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "WriteInt16", address, 1, value.ToString());
            return package;
        }

        /// <summary>
        /// 异步写入16位无符号整数 (功能码 0x06)
        /// </summary>
        public async Task<Package<ushort>> WriteUInt16Async(string address, ushort value)
        {
            var package = new Package<ushort> { Address = address, Value = value };
            try
            {
                package.SendBuff = new[] { BuildWriteSingleRegisterMessage(address, value) };
                package = await SendReceiveAsync(package);
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
                if (ex is IOException || ex is SocketException || ex is TimeoutException)
                    OnDisconnection(ex);
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "WriteUInt16", address, 1, value.ToString());
            return package;
        }

        /// <summary>
        /// 异步写入32位有符号整数 (功能码 0x10)
        /// </summary>
        public async Task<Package<int>> WriteInt32Async(string address, int value)
        {
            var package = new Package<int> { Address = address, Value = value };
            try
            {
                var bytes = BitConverter.GetBytes(value);
                var convertedBytes = ConvertByteOrderBatch(bytes, 4, Order);
                package.SendBuff = new[] { BuildWriteMultipleRegistersMessage(address, convertedBytes) };
                package = await SendReceiveAsync(package);
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
                if (ex is IOException || ex is SocketException || ex is TimeoutException)
                    OnDisconnection(ex);
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "WriteInt32", address, 2, value.ToString());
            return package;
        }

        /// <summary>
        /// 异步写入32位无符号整数 (功能码 0x10)
        /// </summary>
        public async Task<Package<uint>> WriteUInt32Async(string address, uint value)
        {
            var package = new Package<uint> { Address = address, Value = value };
            try
            {
                var bytes = BitConverter.GetBytes(value);
                var convertedBytes = ConvertByteOrderBatch(bytes, 4, Order);
                package.SendBuff = new[] { BuildWriteMultipleRegistersMessage(address, convertedBytes) };
                package = await SendReceiveAsync(package);
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
                if (ex is IOException || ex is SocketException || ex is TimeoutException)
                    OnDisconnection(ex);
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "WriteUInt32", address, 2, value.ToString());
            return package;
        }

        /// <summary>
        /// 异步写入64位有符号整数 (功能码 0x10)
        /// </summary>
        public async Task<Package<long>> WriteInt64Async(string address, long value)
        {
            var package = new Package<long> { Address = address, Value = value };
            try
            {
                var bytes = BitConverter.GetBytes(value);
                var convertedBytes = ConvertByteOrderBatch(bytes, 8, Order);
                package.SendBuff = new[] { BuildWriteMultipleRegistersMessage(address, convertedBytes) };
                package = await SendReceiveAsync(package);
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
                if (ex is IOException || ex is SocketException || ex is TimeoutException)
                    OnDisconnection(ex);
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "WriteInt64", address, 4, value.ToString());
            return package;
        }

        /// <summary>
        /// 异步写入32位浮点数 (功能码 0x10)
        /// </summary>
        public async Task<Package<float>> WriteFloatAsync(string address, float value)
        {
            var package = new Package<float> { Address = address, Value = value };
            try
            {
                var bytes = BitConverter.GetBytes(value);
                var convertedBytes = ConvertByteOrderBatch(bytes, 4, Order);
                package.SendBuff = new[] { BuildWriteMultipleRegistersMessage(address, convertedBytes) };
                package = await SendReceiveAsync(package);
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
                if (ex is IOException || ex is SocketException || ex is TimeoutException)
                    OnDisconnection(ex);
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "WriteFloat", address, 2, value.ToString("F4"));
            return package;
        }

        /// <summary>
        /// 异步写入64位双精度浮点数 (功能码 0x10)
        /// </summary>
        public async Task<Package<double>> WriteDoubleAsync(string address, double value)
        {
            var package = new Package<double> { Address = address, Value = value };
            try
            {
                var bytes = BitConverter.GetBytes(value);
                var convertedBytes = ConvertByteOrderBatch(bytes, 8, Order);
                package.SendBuff = new[] { BuildWriteMultipleRegistersMessage(address, convertedBytes) };
                package = await SendReceiveAsync(package);
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
                if (ex is IOException || ex is SocketException || ex is TimeoutException)
                    OnDisconnection(ex);
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "WriteDouble", address, 4, value.ToString("F6"));
            return package;
        }

        /// <summary>
        /// 异步写入字符串（ASCII编码，功能码 0x10）
        /// </summary>
        /// <param name="address">起始地址</param>
        /// <param name="value">要写入的字符串</param>
        /// <param name="maxLength">最大长度（字节数，必须为偶数）</param>
        public async Task<Package<string>> WriteStringAsync(string address, string value, int maxLength)
        {
            var package = new Package<string> { Address = address, Value = value };
            try
            {
                if (maxLength % 2 != 0)
                    maxLength++;

                var stringBytes = Encoding.ASCII.GetBytes(value ?? string.Empty);
                var paddedBytes = new byte[maxLength];
                Array.Copy(stringBytes, paddedBytes, Math.Min(stringBytes.Length, maxLength));
                var outputBytes = new byte[maxLength];
                switch (Order)
                {
                    case ByteOrder.ABCD:
                    case ByteOrder.CDAB:
                        Array.Copy(paddedBytes, outputBytes, maxLength);
                        break;

                    case ByteOrder.BADC:
                    case ByteOrder.DCBA:
                        for (int i = 0; i < maxLength; i += 2)
                        {
                            outputBytes[i] = paddedBytes[i + 1];
                            outputBytes[i + 1] = paddedBytes[i];
                        }
                        break;
                }
                package.SendBuff = new[] { BuildWriteMultipleRegistersMessage(address, outputBytes) };
                package = await SendReceiveAsync(package);
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
                if (ex is IOException || ex is SocketException || ex is TimeoutException)
                    OnDisconnection(ex);
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "WriteString", address, maxLength / 2, value ?? string.Empty);
            return package;
        }

        /// <summary>
        /// 异步写入多个16位整数 (功能码 0x10)
        /// </summary>
        public async Task<Package<short[]>> WriteInt16ArrayAsync(string address, short[] values)
        {
            var package = new Package<short[]> { Address = address, Value = values };
            try
            {
                var bytes = new byte[values.Length * 2];
                for (int i = 0; i < values.Length; i++)
                {
                    var valueBytes = BitConverter.GetBytes(values[i]);
                    var convertedBytes = ConvertByteOrderBatch(valueBytes, 2, Order);
                    Array.Copy(convertedBytes, 0, bytes, i * 2, 2);
                }
                package.SendBuff = new[] { BuildWriteMultipleRegistersMessage(address, bytes) };
                package = await SendReceiveAsync(package);
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
                if (ex is IOException || ex is SocketException || ex is TimeoutException)
                    OnDisconnection(ex);
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "WriteInt16Array", address, values.Length, values.Length.ToString());
            return package;
        }

        /// <summary>
        /// 异步写入多个16位无符号整数 (功能码 0x10)
        /// </summary>
        public async Task<Package<ushort[]>> WriteUInt16ArrayAsync(string address, ushort[] values)
        {
            var package = new Package<ushort[]> { Address = address, Value = values };
            try
            {
                var bytes = new byte[values.Length * 2];
                for (int i = 0; i < values.Length; i++)
                {
                    var valueBytes = BitConverter.GetBytes(values[i]);
                    var convertedBytes = ConvertByteOrderBatch(valueBytes, 2, Order);
                    Array.Copy(convertedBytes, 0, bytes, i * 2, 2);
                }
                package.SendBuff = new[] { BuildWriteMultipleRegistersMessage(address, bytes) };
                package = await SendReceiveAsync(package);
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
                if (ex is IOException || ex is SocketException || ex is TimeoutException)
                    OnDisconnection(ex);
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "WriteUInt16Array", address, values.Length, values.Length.ToString());
            return package;
        }

        /// <summary>
        /// 异步写入多个32位有符号整数 (功能码 0x10)
        /// </summary>
        public async Task<Package<int[]>> WriteInt32ArrayAsync(string address, int[] values)
        {
            var package = new Package<int[]> { Address = address, Value = values };
            try
            {
                var bytes = new byte[values.Length * 4];
                for (int i = 0; i < values.Length; i++)
                {
                    var valueBytes = BitConverter.GetBytes(values[i]);
                    var convertedBytes = ConvertByteOrderBatch(valueBytes, 4, Order);
                    Array.Copy(convertedBytes, 0, bytes, i * 4, 4);
                }
                package.SendBuff = new[] { BuildWriteMultipleRegistersMessage(address, bytes) };
                package = await SendReceiveAsync(package);
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
                if (ex is IOException || ex is SocketException || ex is TimeoutException)
                    OnDisconnection(ex);
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "WriteInt32Array", address, values.Length * 2, values.Length.ToString());
            return package;
        }

        /// <summary>
        /// 异步写入多个32位无符号整数 (功能码 0x10)
        /// </summary>
        public async Task<Package<uint[]>> WriteUInt32ArrayAsync(string address, uint[] values)
        {
            var package = new Package<uint[]> { Address = address, Value = values };
            try
            {
                var bytes = new byte[values.Length * 4];
                for (int i = 0; i < values.Length; i++)
                {
                    var valueBytes = BitConverter.GetBytes(values[i]);
                    var convertedBytes = ConvertByteOrderBatch(valueBytes, 4, Order);
                    Array.Copy(convertedBytes, 0, bytes, i * 4, 4);
                }
                package.SendBuff = new[] { BuildWriteMultipleRegistersMessage(address, bytes) };
                package = await SendReceiveAsync(package);
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
                if (ex is IOException || ex is SocketException || ex is TimeoutException)
                    OnDisconnection(ex);
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "WriteUInt32Array", address, values.Length * 2, values.Length.ToString());
            return package;
        }

        /// <summary>
        /// 异步写入多个浮点数 (功能码 0x10)
        /// </summary>
        public async Task<Package<float[]>> WriteFloatArrayAsync(string address, float[] values)
        {
            var package = new Package<float[]> { Address = address, Value = values };

            try
            {
                var bytes = new byte[values.Length * 4];
                for (int i = 0; i < values.Length; i++)
                {
                    var floatBytes = BitConverter.GetBytes(values[i]);
                    var convertedBytes = ConvertByteOrderBatch(floatBytes, 4, Order);
                    Array.Copy(convertedBytes, 0, bytes, i * 4, 4);
                }
                package.SendBuff = new[] { BuildWriteMultipleRegistersMessage(address, bytes) };
                package = await SendReceiveAsync(package);
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
                if (ex is IOException || ex is SocketException || ex is TimeoutException)
                    OnDisconnection(ex);
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "WriteFloatArray", address, values.Length * 2, values.Length.ToString());
            return package;
        }

        /// <summary>
        /// 异步写入多个双精度浮点数 (功能码 0x10)
        /// </summary>
        public async Task<Package<double[]>> WriteDoubleArrayAsync(string address, double[] values)
        {
            var package = new Package<double[]> { Address = address, Value = values };
            try
            {
                var bytes = new byte[values.Length * 8];
                for (int i = 0; i < values.Length; i++)
                {
                    var doubleBytes = BitConverter.GetBytes(values[i]);
                    var convertedBytes = ConvertByteOrderBatch(doubleBytes, 8, Order);
                    Array.Copy(convertedBytes, 0, bytes, i * 8, 8);
                }
                package.SendBuff = new[] { BuildWriteMultipleRegistersMessage(address, bytes) };
                package = await SendReceiveAsync(package);
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
                if (ex is IOException || ex is SocketException || ex is TimeoutException)
                    OnDisconnection(ex);
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "WriteDoubleArray", address, values.Length * 4, values.Length.ToString());
            return package;
        }

        #endregion

        #region 同步实现方法

        private Package<bool> ReadBitSync(string address, byte function = 0x03)
        {
            var package = new Package<bool> { Address = address };
            string result = "Null";
            try
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("功能码必须是 0x03 或 0x04", nameof(function));
                if (string.IsNullOrWhiteSpace(address))
                    throw new ArgumentException("地址不能为空", nameof(address));
                string[] parts = address.Split('.');
                if (parts.Length != 2)
                    throw new FormatException("地址格式必须为 寄存器.位 例如 100.2");
                if (!int.TryParse(parts[0], out int registerAddress))
                    throw new FormatException("寄存器地址格式错误");
                if (!int.TryParse(parts[1], out int bitIndex))
                    throw new FormatException("位索引格式错误");
                if (bitIndex < 0 || bitIndex > 15)
                    throw new ArgumentOutOfRangeException(nameof(address), "bitIndex 必须在 0~15 之间");
                package.SendBuff = BuildReadMessages(function, registerAddress.ToString(), 1);
                package = SendReceiveSync(package);
                if (!package.IsSuccess)
                    return package;
                if (package.DataBuff.Length != 2)
                    throw new Exception("返回数据长度异常");
                var bytes = ConvertByteOrderBatch(package.DataBuff, 2, Order);
                ushort registerValue = BitConverter.ToUInt16(bytes, 0);
                bool bitValue = (registerValue & (1 << bitIndex)) != 0;
                package.Value = bitValue;
                package.IsSuccess = true;
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
            }
            OnInteractionEvent(package.ElapsedTime,
                               package.IsSuccess,
                               "ReadSingleBit",
                               address,
                               1,
                               result);
            return package;
        }

        private Package<bool> ReadBoolSync(string address, byte function = 0x01)
        {
            var package = new Package<bool> { Address = address };
            try
            {
                if (function != 0x01 && function != 0x02)
                    throw new ArgumentException("功能码必须是 0x01 或 0x02", nameof(function));
                package.SendBuff = BuildReadMessages(function, address, 1);
                package = SendReceiveSync(package);
                if (package.IsSuccess && package.DataBuff.Length >= 1)
                    package.Value = (package.DataBuff[0] & 0x01) != 0;
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "ReadBool", address, 1, package.Value.ToString());
            return package;
        }

        private Package<bool[]> ReadBoolArraySync(string address, int count, byte function = 0x01)
        {
            var package = new Package<bool[]> { Address = address };
            try
            {
                if (function != 0x01 && function != 0x02)
                    throw new ArgumentException("功能码必须是 0x01 或 0x02", nameof(function));
                package.SendBuff = BuildReadMessages(function, address, count);
                package = SendReceiveSync(package);
                if (package.IsSuccess && package.DataBuff.Length >= (count + 7) / 8)
                {
                    var values = new bool[count];
                    for (int i = 0; i < count; i++)
                        values[i] = (package.DataBuff[i / 8] & (1 << (i % 8))) != 0;
                    package.Value = values;
                }
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "ReadBoolArray", address, count, package.Value?.Length.ToString() ?? "0");
            return package;
        }

        private Package<short> ReadInt16Sync(string address, byte function = 0x03)
        {
            var package = new Package<short> { Address = address };
            try
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("功能码必须是 0x03 或 0x04", nameof(function));
                package.SendBuff = BuildReadMessages(function, address, 1);
                package = SendReceiveSync(package);
                if (package.IsSuccess && package.DataBuff.Length >= 2)
                {
                    var bytes = ConvertByteOrderBatch(package.DataBuff, 2, Order);
                    package.Value = BitConverter.ToInt16(bytes, 0);
                }
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "ReadInt16", address, 1, package.Value.ToString());
            return package;
        }

        private Package<ushort> ReadUInt16Sync(string address, byte function = 0x03)
        {
            var package = new Package<ushort> { Address = address };
            try
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("功能码必须是 0x03 或 0x04", nameof(function));
                package.SendBuff = BuildReadMessages(function, address, 1);
                package = SendReceiveSync(package);
                if (package.IsSuccess && package.DataBuff.Length >= 2)
                {
                    var bytes = ConvertByteOrderBatch(package.DataBuff, 2, Order);
                    package.Value = BitConverter.ToUInt16(bytes, 0);
                }
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "ReadUInt16", address, 1, package.Value.ToString());
            return package;
        }

        private Package<int> ReadInt32Sync(string address, byte function = 0x03)
        {
            var package = new Package<int> { Address = address };
            try
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("功能码必须是 0x03 或 0x04", nameof(function));
                package.SendBuff = BuildReadMessages(function, address, 2);
                package = SendReceiveSync(package);
                if (package.IsSuccess && package.DataBuff.Length >= 4)
                {
                    var bytes = ConvertByteOrderBatch(package.DataBuff, 4, Order);
                    package.Value = BitConverter.ToInt32(bytes, 0);
                }
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "ReadInt32", address, 2, package.Value.ToString());
            return package;
        }

        private Package<uint> ReadUInt32Sync(string address, byte function = 0x03)
        {
            var package = new Package<uint> { Address = address };
            try
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("功能码必须是 0x03 或 0x04", nameof(function));
                package.SendBuff = BuildReadMessages(function, address, 2);
                package = SendReceiveSync(package);
                if (package.IsSuccess && package.DataBuff.Length >= 4)
                {
                    var bytes = ConvertByteOrderBatch(package.DataBuff, 4, Order);
                    package.Value = BitConverter.ToUInt32(bytes, 0);
                }
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "ReadUInt32", address, 2, package.Value.ToString());
            return package;
        }

        private Package<long> ReadInt64Sync(string address, byte function = 0x03)
        {
            var package = new Package<long> { Address = address };
            try
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("功能码必须是 0x03 或 0x04", nameof(function));
                package.SendBuff = BuildReadMessages(function, address, 4);
                package = SendReceiveSync(package);
                if (package.IsSuccess && package.DataBuff.Length >= 8)
                {
                    var bytes = ConvertByteOrderBatch(package.DataBuff, 8, Order);
                    package.Value = BitConverter.ToInt64(bytes, 0);
                }
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "ReadInt64", address, 4, package.Value.ToString());
            return package;
        }

        private Package<float> ReadFloatSync(string address, byte function = 0x03)
        {
            var package = new Package<float> { Address = address };
            try
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("功能码必须是 0x03 或 0x04", nameof(function));
                package.SendBuff = BuildReadMessages(function, address, 2);
                package = SendReceiveSync(package);
                if (package.IsSuccess && package.DataBuff.Length >= 4)
                {
                    var bytes = ConvertByteOrderBatch(package.DataBuff, 4, Order);
                    package.Value = BitConverter.ToSingle(bytes, 0);
                }
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "ReadFloat", address, 2, package.Value.ToString());
            return package;
        }

        private Package<double> ReadDoubleSync(string address, byte function = 0x03)
        {
            var package = new Package<double> { Address = address };
            try
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("功能码必须是 0x03 或 0x04", nameof(function));
                package.SendBuff = BuildReadMessages(function, address, 4);
                package = SendReceiveSync(package);
                if (package.IsSuccess && package.DataBuff.Length >= 8)
                {
                    var bytes = ConvertByteOrderBatch(package.DataBuff, 8, Order);
                    package.Value = BitConverter.ToDouble(bytes, 0);
                }
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "ReadDouble", address, 4, package.Value.ToString());
            return package;
        }

        private Package<string> ReadStringSync(string address, int length, byte function = 0x03)
        {
            var package = new Package<string> { Address = address };
            try
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("功能码必须是 0x03 或 0x04", nameof(function));
                int registerCount = (length + 1) / 2;
                package.SendBuff = BuildReadMessages(function, address, registerCount);
                package = SendReceiveSync(package);
                if (package.IsSuccess && package.DataBuff.Length > 0)
                {
                    var stringBytes = new byte[Math.Min(package.DataBuff.Length, length)];
                    var bytes = package.DataBuff.Take(length).ToArray();
                    var str = Encoding.ASCII.GetString(bytes).TrimEnd('\0');
                    if (ReverseString)
                    {
                        str = new string(str.Reverse().ToArray());
                    }
                    package.Value = str;
                }
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "ReadString", address, length, package.Value ?? string.Empty);
            return package;
        }

        private Package<bool[]> ReadBitArraySync(string address, int registerLength, byte function = 0x03)
        {
            var package = new Package<bool[]> { Address = address };
            string result = "Null";
            try
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("功能码必须是 0x03 或 0x04", nameof(function));
                if (registerLength <= 0)
                    throw new ArgumentException("registerLength 必须大于 0", nameof(registerLength));
                package.SendBuff = BuildReadMessages(function, address, registerLength);
                package = SendReceiveSync(package);
                if (package.IsSuccess && package.DataBuff.Length >= registerLength * 2)
                {
                    var bytes = ConvertByteOrderBatch(package.DataBuff, 2, Order);
                    int totalBits = registerLength * 16;
                    bool[] bitStates = new bool[totalBits];
                    for (int i = 0; i < registerLength; i++)
                    {
                        ushort currentRegister = BitConverter.ToUInt16(bytes, i * 2);
                        for (int j = 0; j < 16; j++)
                        {
                            int bitIndex = i * 16 + j;
                            bitStates[bitIndex] = ((currentRegister & (1 << j)) != 0 ? true : false);
                        }
                    }
                    package.Value = bitStates;
                    result = string.Join(", ",
                        bitStates.Select((v, i) => $"{i}={v}"));
                }
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
            }
            OnInteractionEvent(package.ElapsedTime,
                               package.IsSuccess,
                               "ReadBits",
                               address,
                               registerLength,
                               result);
            return package;
        }

        private Package<short[]> ReadInt16ArraySync(string address, int count, byte function = 0x03)
        {
            var package = new Package<short[]> { Address = address };
            try
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("功能码必须是 0x03 或 0x04", nameof(function));
                package.SendBuff = BuildReadMessages(function, address, count);
                package = SendReceiveSync(package);
                if (package.IsSuccess && package.DataBuff.Length >= count * 2)
                {
                    var values = new short[count];
                    for (int i = 0; i < count; i++)
                    {
                        var bytes = new byte[2];
                        Array.Copy(package.DataBuff, i * 2, bytes, 0, 2);
                        var convertedBytes = ConvertByteOrderBatch(bytes, 2, Order);
                        values[i] = BitConverter.ToInt16(convertedBytes, 0);
                    }
                    package.Value = values;
                }
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "ReadInt16Array", address, count, package.Value?.Length.ToString() ?? "0");
            return package;
        }

        private Package<ushort[]> ReadUInt16ArraySync(string address, int count, byte function = 0x03)
        {
            var package = new Package<ushort[]> { Address = address };
            try
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("功能码必须是 0x03 或 0x04", nameof(function));
                package.SendBuff = BuildReadMessages(function, address, count);
                package = SendReceiveSync(package);
                if (package.IsSuccess && package.DataBuff.Length >= count * 2)
                {
                    var values = new ushort[count];
                    for (int i = 0; i < count; i++)
                    {
                        var bytes = new byte[2];
                        Array.Copy(package.DataBuff, i * 2, bytes, 0, 2);
                        var convertedBytes = ConvertByteOrderBatch(bytes, 2, Order);
                        values[i] = BitConverter.ToUInt16(convertedBytes, 0);
                    }
                    package.Value = values;
                }
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "ReadUInt16Array", address, count, package.Value?.Length.ToString() ?? "0");
            return package;
        }

        private Package<int[]> ReadInt32ArraySync(string address, int count, byte function = 0x03)
        {
            var package = new Package<int[]> { Address = address };
            try
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("功能码必须是 0x03 或 0x04", nameof(function));
                package.SendBuff = BuildReadMessages(function, address, count * 2);
                package = SendReceiveSync(package);
                if (package.IsSuccess && package.DataBuff.Length >= count * 4)
                {
                    var values = new int[count];
                    for (int i = 0; i < count; i++)
                    {
                        var bytes = new byte[4];
                        Array.Copy(package.DataBuff, i * 4, bytes, 0, 4);
                        var convertedBytes = ConvertByteOrderBatch(bytes, 4, Order);
                        values[i] = BitConverter.ToInt32(convertedBytes, 0);
                    }
                    package.Value = values;
                }
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "ReadInt32Array", address, count * 2, package.Value?.Length.ToString() ?? "0");
            return package;
        }

        private Package<uint[]> ReadUInt32ArraySync(string address, int count, byte function = 0x03)
        {
            var package = new Package<uint[]> { Address = address };
            try
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("功能码必须是 0x03 或 0x04", nameof(function));
                package.SendBuff = BuildReadMessages(function, address, count * 2);
                package = SendReceiveSync(package);
                if (package.IsSuccess && package.DataBuff.Length >= count * 4)
                {
                    var values = new uint[count];
                    for (int i = 0; i < count; i++)
                    {
                        var bytes = new byte[4];
                        Array.Copy(package.DataBuff, i * 4, bytes, 0, 4);
                        var convertedBytes = ConvertByteOrderBatch(bytes, 4, Order);
                        values[i] = BitConverter.ToUInt32(convertedBytes, 0);
                    }
                    package.Value = values;
                }
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "ReadUInt32Array", address, count * 2, package.Value?.Length.ToString() ?? "0");
            return package;
        }

        private Package<float[]> ReadFloatArraySync(string address, int count, byte function = 0x03)
        {
            var package = new Package<float[]> { Address = address };
            try
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("功能码必须是 0x03 或 0x04", nameof(function));
                package.SendBuff = BuildReadMessages(function, address, count * 2);
                package = SendReceiveSync(package);
                if (package.IsSuccess && package.DataBuff.Length >= count * 4)
                {
                    var values = new float[count];
                    for (int i = 0; i < count; i++)
                    {
                        var bytes = new byte[4];
                        Array.Copy(package.DataBuff, i * 4, bytes, 0, 4);
                        var convertedBytes = ConvertByteOrderBatch(bytes, 4, Order);
                        values[i] = BitConverter.ToSingle(convertedBytes, 0);
                    }
                    package.Value = values;
                }
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "ReadFloatArray", address, count * 2, package.Value?.Length.ToString() ?? "0");
            return package;
        }

        private Package<double[]> ReadDoubleArraySync(string address, int count, byte function = 0x03)
        {
            var package = new Package<double[]> { Address = address };
            try
            {
                if (function != 0x03 && function != 0x04)
                    throw new ArgumentException("功能码必须是 0x03 或 0x04", nameof(function));
                package.SendBuff = BuildReadMessages(function, address, count * 4);
                package = SendReceiveSync(package);
                if (package.IsSuccess && package.DataBuff.Length >= count * 8)
                {
                    var values = new double[count];
                    for (int i = 0; i < count; i++)
                    {
                        var bytes = new byte[8];
                        Array.Copy(package.DataBuff, i * 8, bytes, 0, 8);
                        var convertedBytes = ConvertByteOrderBatch(bytes, 8, Order);
                        values[i] = BitConverter.ToDouble(convertedBytes, 0);
                    }
                    package.Value = values;
                }
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "ReadDoubleArray", address, count * 4, package.Value?.Length.ToString() ?? "0");
            return package;
        }

        private Package<bool> WriteBoolSync(string address, bool value)
        {
            var package = new Package<bool> { Address = address, Value = value };
            try
            {
                package.SendBuff = new[] { BuildWriteSingleCoilMessage(address, value) };
                package = SendReceiveSync(package);
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "WriteBool", address, 1, value.ToString());
            return package;
        }

        private Package<bool[]> WriteBoolArraySync(string address, bool[] values)
        {
            var package = new Package<bool[]> { Address = address, Value = values };
            try
            {
                package.SendBuff = new[] { BuildWriteMultipleCoilsMessage(address, values) };
                package = SendReceiveSync(package);
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "WriteBoolArray", address, values.Length, values.Length.ToString());
            return package;
        }

        private Package<short> WriteInt16Sync(string address, short value)
        {
            var package = new Package<short> { Address = address, Value = value };
            try
            {
                package.SendBuff = new[] { BuildWriteSingleRegisterMessage(address, (ushort)value) };
                package = SendReceiveSync(package);
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "WriteInt16", address, 1, value.ToString());
            return package;
        }

        private Package<ushort> WriteUInt16Sync(string address, ushort value)
        {
            var package = new Package<ushort> { Address = address, Value = value };
            try
            {
                package.SendBuff = new[] { BuildWriteSingleRegisterMessage(address, value) };
                package = SendReceiveSync(package);
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "WriteUInt16", address, 1, value.ToString());
            return package;
        }

        private Package<bool> WriteSingleBitSync(string address, int bitIndex, bool value)
        {
            var package = new Package<bool> { Address = address, Value = value };
            bool connectionLost = false;
            try
            {
                if (bitIndex < 0 || bitIndex > 15)
                    throw new ArgumentException("bitIndex必须在0~15之间");
                if (!IsConnected)
                {
                    package.IsSuccess = false;
                    package.ErrorMessage = "未连接到服务器";
                    return package;
                }
                int lockTimeout = ReadTimeout > 0 ? ReadTimeout : Timeout.Infinite;
                if (!_sendLock.Wait(lockTimeout, _cancellationTokenSource.Token))
                {
                    package.IsSuccess = false;
                    package.ErrorMessage = $"等待信号量超时({ReadTimeout}ms)";
                    return package;
                }
                try
                {
                    var client = _tcpClient;
                    if (client == null || !client.Connected || _isDisposed)
                    {
                        package.IsSuccess = false;
                        package.ErrorMessage = "未连接到服务器";
                        return package;
                    }
                    var stream = client.GetStream();
                    var readPkg = new Package<ushort>
                    {
                        SendBuff = BuildReadMessages(0x03, address, 1)
                    };
                    readPkg = SendReceiveCore(stream, readPkg);
                    if (!readPkg.IsSuccess || readPkg.DataBuff == null || readPkg.DataBuff.Length < 2)
                    {
                        package.IsSuccess = false;
                        package.ErrorMessage = readPkg.ErrorMessage ?? "读取寄存器失败";
                        return package;
                    }
                    ushort registerValue = (ushort)((readPkg.DataBuff[0] << 8) | readPkg.DataBuff[1]);
                    if (value)
                        registerValue |= (ushort)(1 << bitIndex);   // 置1
                    else
                        registerValue &= (ushort)~(1 << bitIndex);  // 置0

                    // 写回寄存器
                    package.SendBuff = new[] { BuildWriteSingleRegisterMessage(address, registerValue) };
                    package = SendReceiveCore(stream, package);
                }
                catch (Exception ex)
                {
                    package.IsSuccess = false;
                    package.ErrorMessage = ex.Message;
                    if (ex is IOException || ex is SocketException || ex is TimeoutException)
                        connectionLost = true;
                }
                finally
                {
                    _sendLock.Release();
                }
                if (connectionLost)
                    OnConnectionLost();
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
            }
            OnInteractionEvent(package.ElapsedTime,
                               package.IsSuccess,
                               "WriteSingleBit",
                               address,
                               bitIndex,
                               value.ToString());
            return package;
        }

        private Package<int> WriteInt32Sync(string address, int value)
        {
            var package = new Package<int> { Address = address, Value = value };
            try
            {
                var bytes = BitConverter.GetBytes(value);
                var convertedBytes = ConvertByteOrderBatch(bytes, 4, Order);
                package.SendBuff = new[] { BuildWriteMultipleRegistersMessage(address, convertedBytes) };
                package = SendReceiveSync(package);
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "WriteInt32", address, 2, value.ToString());
            return package;
        }

        private Package<uint> WriteUInt32Sync(string address, uint value)
        {
            var package = new Package<uint> { Address = address, Value = value };
            try
            {
                var bytes = BitConverter.GetBytes(value);
                var convertedBytes = ConvertByteOrderBatch(bytes, 4, Order);
                package.SendBuff = new[] { BuildWriteMultipleRegistersMessage(address, convertedBytes) };
                package = SendReceiveSync(package);
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "WriteUInt32", address, 2, value.ToString());
            return package;
        }

        private Package<long> WriteInt64Sync(string address, long value)
        {
            var package = new Package<long> { Address = address, Value = value };
            try
            {
                var bytes = BitConverter.GetBytes(value);
                var convertedBytes = ConvertByteOrderBatch(bytes, 8, Order);
                package.SendBuff = new[] { BuildWriteMultipleRegistersMessage(address, convertedBytes) };
                package = SendReceiveSync(package);
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "WriteInt64", address, 4, value.ToString());
            return package;
        }

        private Package<float> WriteFloatSync(string address, float value)
        {
            var package = new Package<float> { Address = address, Value = value };
            try
            {
                var bytes = BitConverter.GetBytes(value);
                var convertedBytes = ConvertByteOrderBatch(bytes, 4, Order);
                package.SendBuff = new[] { BuildWriteMultipleRegistersMessage(address, convertedBytes) };
                package = SendReceiveSync(package);
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "WriteFloat", address, 2, value.ToString());
            return package;
        }

        private Package<double> WriteDoubleSync(string address, double value)
        {
            var package = new Package<double> { Address = address, Value = value };
            try
            {
                var bytes = BitConverter.GetBytes(value);
                var convertedBytes = ConvertByteOrderBatch(bytes, 8, Order);
                package.SendBuff = new[] { BuildWriteMultipleRegistersMessage(address, convertedBytes) };
                package = SendReceiveSync(package);
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "WriteDouble", address, 4, value.ToString());
            return package;
        }

        private Package<string> WriteStringSync(string address, string value, int maxLength)
        {
            var package = new Package<string> { Address = address, Value = value };
            try
            {
                if (maxLength % 2 != 0)
                    maxLength++;

                var stringBytes = Encoding.ASCII.GetBytes(value ?? string.Empty);
                var paddedBytes = new byte[maxLength];
                Array.Copy(stringBytes, paddedBytes, Math.Min(stringBytes.Length, maxLength));
                var outputBytes = new byte[maxLength];
                switch (Order)
                {
                    case ByteOrder.ABCD:
                    case ByteOrder.CDAB:
                        Array.Copy(paddedBytes, outputBytes, maxLength);
                        break;

                    case ByteOrder.BADC:
                    case ByteOrder.DCBA:
                        for (int i = 0; i < maxLength; i += 2)
                        {
                            outputBytes[i] = paddedBytes[i + 1];
                            outputBytes[i + 1] = paddedBytes[i];
                        }
                        break;
                }

                package.SendBuff = new[] { BuildWriteMultipleRegistersMessage(address, outputBytes) };
                package = SendReceiveSync(package);
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "WriteString", address, maxLength / 2, value ?? string.Empty);
            return package;
        }

        private Package<short[]> WriteInt16ArraySync(string address, short[] values)
        {
            var package = new Package<short[]> { Address = address, Value = values };
            try
            {
                var bytes = new byte[values.Length * 2];
                for (int i = 0; i < values.Length; i++)
                {
                    var valueBytes = BitConverter.GetBytes(values[i]);
                    var convertedBytes = ConvertByteOrderBatch(valueBytes, 2, Order);
                    Array.Copy(convertedBytes, 0, bytes, i * 2, 2);
                }
                package.SendBuff = new[] { BuildWriteMultipleRegistersMessage(address, bytes) };
                package = SendReceiveSync(package);
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "WriteInt16Array", address, values.Length, values.Length.ToString());
            return package;
        }

        private Package<ushort[]> WriteUInt16ArraySync(string address, ushort[] values)
        {
            var package = new Package<ushort[]> { Address = address, Value = values };
            try
            {
                var bytes = new byte[values.Length * 2];
                for (int i = 0; i < values.Length; i++)
                {
                    var valueBytes = BitConverter.GetBytes(values[i]);
                    var convertedBytes = ConvertByteOrderBatch(valueBytes, 2, Order);
                    Array.Copy(convertedBytes, 0, bytes, i * 2, 2);
                }
                package.SendBuff = new[] { BuildWriteMultipleRegistersMessage(address, bytes) };
                package = SendReceiveSync(package);
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "WriteUInt16Array", address, values.Length, values.Length.ToString());
            return package;
        }

        private Package<bool[]> WriteBitArraySync(string address, bool[] values)
        {
            var package = new Package<bool[]> { Address = address, Value = values };

            try
            {
                if (values == null || values.Length == 0)
                    throw new ArgumentException("values不能为空");
                int registerCount = (values.Length + 15) / 16;
                ushort[] registers = new ushort[registerCount];
                for (int i = 0; i < values.Length; i++)
                {
                    if (values[i])
                    {
                        int registerIndex = i / 16;
                        int bitIndex = i % 16;

                        registers[registerIndex] |= (ushort)(1 << bitIndex);
                    }
                }
                byte[] rawBytes = registers
                    .SelectMany(r => BitConverter.GetBytes(r))
                    .ToArray();
                byte[] bytes = ConvertByteOrderBatch(rawBytes, 2, Order);
                package.SendBuff = new[] { BuildWriteMultipleRegistersMessage(address, bytes) };
                package = SendReceiveSync(package);
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
            }

            OnInteractionEvent(package.ElapsedTime,
                               package.IsSuccess,
                               "WriteBitArraySync",
                               address,
                               values?.Length ?? 0,
                               values == null ? "null" : string.Join(",", values.Select((v, i) => $"{i}={v}")));

            return package;
        }

        private Package<int[]> WriteInt32ArraySync(string address, int[] values)
        {
            var package = new Package<int[]> { Address = address, Value = values };
            try
            {
                var bytes = new byte[values.Length * 4];
                for (int i = 0; i < values.Length; i++)
                {
                    var valueBytes = BitConverter.GetBytes(values[i]);
                    var convertedBytes = ConvertByteOrderBatch(valueBytes, 4, Order);
                    Array.Copy(convertedBytes, 0, bytes, i * 4, 4);
                }
                package.SendBuff = new[] { BuildWriteMultipleRegistersMessage(address, bytes) };
                package = SendReceiveSync(package);
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "WriteInt32Array", address, values.Length * 2, values.Length.ToString());
            return package;
        }

        private Package<uint[]> WriteUInt32ArraySync(string address, uint[] values)
        {
            var package = new Package<uint[]> { Address = address, Value = values };
            try
            {
                var bytes = new byte[values.Length * 4];
                for (int i = 0; i < values.Length; i++)
                {
                    var valueBytes = BitConverter.GetBytes(values[i]);
                    var convertedBytes = ConvertByteOrderBatch(valueBytes, 4, Order);
                    Array.Copy(convertedBytes, 0, bytes, i * 4, 4);
                }
                package.SendBuff = new[] { BuildWriteMultipleRegistersMessage(address, bytes) };
                package = SendReceiveSync(package);
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "WriteUInt32Array", address, values.Length * 2, values.Length.ToString());
            return package;
        }

        private Package<float[]> WriteFloatArraySync(string address, float[] values)
        {
            var package = new Package<float[]> { Address = address, Value = values };
            try
            {
                var bytes = new byte[values.Length * 4];
                for (int i = 0; i < values.Length; i++)
                {
                    var valueBytes = BitConverter.GetBytes(values[i]);
                    var convertedBytes = ConvertByteOrderBatch(valueBytes, 4, Order);
                    Array.Copy(convertedBytes, 0, bytes, i * 4, 4);
                }
                package.SendBuff = new[] { BuildWriteMultipleRegistersMessage(address, bytes) };
                package = SendReceiveSync(package);
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "WriteFloatArray", address, values.Length * 2, values.Length.ToString());
            return package;
        }

        private Package<double[]> WriteDoubleArraySync(string address, double[] values)
        {
            var package = new Package<double[]> { Address = address, Value = values };
            try
            {
                var bytes = new byte[values.Length * 8];
                for (int i = 0; i < values.Length; i++)
                {
                    var valueBytes = BitConverter.GetBytes(values[i]);
                    var convertedBytes = ConvertByteOrderBatch(valueBytes, 8, Order);
                    Array.Copy(convertedBytes, 0, bytes, i * 8, 8);
                }
                package.SendBuff = new[] { BuildWriteMultipleRegistersMessage(address, bytes) };
                package = SendReceiveSync(package);
            }
            catch (Exception ex)
            {
                package.IsSuccess = false;
                package.ErrorMessage = ex.Message;
            }
            OnInteractionEvent(package.ElapsedTime, package.IsSuccess, "WriteDoubleArray", address, values.Length * 4, values.Length.ToString());
            return package;
        }

        #endregion

        #region 同步方法封装

        /// <summary>
        /// 同步连接到服务器
        /// </summary>
        public bool Connect(string targetIP, int targetPort)
        {
            return AsyncNewTcp(targetIP, targetPort);
        }

        /// <summary>
        /// 同步断开连接
        /// </summary>
        public void Disconnect()
        {
            DisconnectAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// 同步读取布尔值
        /// </summary>
        public Package<bool> ReadBool(string address, byte function = 0x01)
        {
            return ReadBoolSync(address, function);
        }

        /// <summary>
        /// 同步读取单个Bit
        /// </summary>
        public Package<bool> ReadBit(string address, byte function = 0x03)
        {
            return ReadBitSync(address, function);
        }

        /// <summary>
        /// 同步读取多个Bit
        /// </summary>
        public Package<bool[]> ReadBitArray(string address, int count, byte function = 0x03)
        {
            return ReadBitArraySync(address, count, function);
        }

        /// <summary>
        /// 同步读取多个布尔值
        /// </summary>
        public Package<bool[]> ReadBoolArray(string address, int count, byte function = 0x01)
        {
            return ReadBoolArraySync(address, count, function);
        }

        /// <summary>
        /// 同步读取16位有符号整数
        /// </summary>
        public Package<short> ReadInt16(string address, byte function = 0x03)
        {
            return ReadInt16Sync(address, function);
        }

        /// <summary>
        /// 同步读取16位无符号整数
        /// </summary>
        public Package<ushort> ReadUInt16(string address, byte function = 0x03)
        {
            return ReadUInt16Sync(address, function);
        }

        /// <summary>
        /// 同步读取32位有符号整数
        /// </summary>
        public Package<int> ReadInt32(string address, byte function = 0x03)
        {
            return ReadInt32Sync(address, function);
        }

        /// <summary>
        /// 同步读取32位无符号整数
        /// </summary>
        public Package<uint> ReadUInt32(string address, byte function = 0x03)
        {
            return ReadUInt32Sync(address, function);
        }

        /// <summary>
        /// 同步读取64位有符号整数
        /// </summary>
        public Package<long> ReadInt64(string address, byte function = 0x03)
        {
            return ReadInt64Sync(address, function);
        }

        /// <summary>
        /// 同步读取32位浮点数
        /// </summary>
        public Package<float> ReadFloat(string address, byte function = 0x03)
        {
            return ReadFloatSync(address, function);
        }

        /// <summary>
        /// 同步读取64位双精度浮点数
        /// </summary>
        public Package<double> ReadDouble(string address, byte function = 0x03)
        {
            return ReadDoubleSync(address, function);
        }

        /// <summary>
        /// 同步读取字符串
        /// </summary>
        public Package<string> ReadString(string address, int length, byte function = 0x03)
        {
            return ReadStringSync(address, length, function);
        }

        /// <summary>
        /// 同步读取多个16位整数
        /// </summary>
        public Package<short[]> ReadInt16Array(string address, int count, byte function = 0x03)
        {
            return ReadInt16ArraySync(address, count, function);
        }

        /// <summary>
        /// 同步读取多个16位无符号整数
        /// </summary>
        public Package<ushort[]> ReadUInt16Array(string address, int count, byte function = 0x03)
        {
            return ReadUInt16ArraySync(address, count, function);
        }

        /// <summary>
        /// 同步读取多个32位有符号整数
        /// </summary>
        public Package<int[]> ReadInt32Array(string address, int count, byte function = 0x03)
        {
            return ReadInt32ArraySync(address, count, function);
        }

        /// <summary>
        /// 同步读取多个32位无符号整数
        /// </summary>
        public Package<uint[]> ReadUInt32Array(string address, int count, byte function = 0x03)
        {
            return ReadUInt32ArraySync(address, count, function);
        }

        /// <summary>
        /// 同步读取多个浮点数
        /// </summary>
        public Package<float[]> ReadFloatArray(string address, int count, byte function = 0x03)
        {
            return ReadFloatArraySync(address, count, function);
        }

        /// <summary>
        /// 同步读取多个双精度浮点数
        /// </summary>
        public Package<double[]> ReadDoubleArray(string address, int count, byte function = 0x03)
        {
            return ReadDoubleArraySync(address, count, function);
        }

        /// <summary>
        /// 同步写入布尔值
        /// </summary>
        public Package<bool> WriteBool(string address, bool value)
        {
            return WriteBoolSync(address, value);
        }

        /// <summary>
        /// 同步写入多个布尔值
        /// </summary>
        public Package<bool[]> WriteBoolArray(string address, bool[] values)
        {
            return WriteBoolArraySync(address, values);
        }

        /// <summary>
        /// 同步写入16位有符号整数
        /// </summary>
        public Package<short> WriteInt16(string address, short value)
        {
            return WriteInt16Sync(address, value);
        }

        /// <summary>
        /// 同步写入单个bit位
        /// </summary>
        public Package<bool> WriteBit(string address, int bitIndex, bool value)
        {
            return WriteSingleBitSync(address, bitIndex, value);
        }

        /// <summary>
        /// 同步写入16位无符号整数
        /// </summary>
        public Package<ushort> WriteUInt16(string address, ushort value)
        {
            return WriteUInt16Sync(address, value);
        }

        /// <summary>
        /// 同步写入32位有符号整数
        /// </summary>
        public Package<int> WriteInt32(string address, int value)
        {
            return WriteInt32Sync(address, value);
        }

        /// <summary>
        /// 同步写入32位无符号整数
        /// </summary>
        public Package<uint> WriteUInt32(string address, uint value)
        {
            return WriteUInt32Sync(address, value);
        }

        /// <summary>
        /// 同步写入64位有符号整数
        /// </summary>
        public Package<long> WriteInt64(string address, long value)
        {
            return WriteInt64Sync(address, value);
        }

        /// <summary>
        /// 同步写入32位浮点数
        /// </summary>
        public Package<float> WriteFloat(string address, float value)
        {
            return WriteFloatSync(address, value);
        }

        /// <summary>
        /// 同步写入64位双精度浮点数
        /// </summary>
        public Package<double> WriteDouble(string address, double value)
        {
            return WriteDoubleSync(address, value);
        }

        /// <summary>
        /// 同步写入字符串
        /// </summary>
        public Package<string> WriteString(string address, string value, int maxLength)
        {
            var stringBytes = Encoding.ASCII.GetBytes(value ?? string.Empty);
            return WriteStringSync(address, value, maxLength);
        }

        /// <summary>
        /// 同步写入字符串
        /// </summary>
        public Package<string> WriteString(string address, string value)
        {
            var stringBytes = Encoding.ASCII.GetBytes(value ?? string.Empty);
            return WriteStringSync(address, value, stringBytes.Length);
        }

        /// <summary>
        /// 同步写入多个16位有符号整数
        /// </summary>
        public Package<short[]> WriteInt16Array(string address, short[] values)
        {
            return WriteInt16ArraySync(address, values);
        }

        /// <summary>
        /// 同步写入多个bit
        /// </summary>
        public Package<bool[]> WriteBitArray(string address, bool[] values)
        {
            return WriteBitArraySync(address, values);
        }

        /// <summary>
        /// 同步写入多个16位无符号整数
        /// </summary>
        public Package<ushort[]> WriteUInt16Array(string address, ushort[] values)
        {
            return WriteUInt16ArraySync(address, values);
        }

        /// <summary>
        /// 同步写入多个32位有符号整数
        /// </summary>
        public Package<int[]> WriteInt32Array(string address, int[] values)
        {
            return WriteInt32ArraySync(address, values);
        }

        /// <summary>
        /// 同步写入多个32位无符号整数
        /// </summary>
        public Package<uint[]> WriteUInt32Array(string address, uint[] values)
        {
            return WriteUInt32ArraySync(address, values);
        }

        /// <summary>
        /// 同步写入多个浮点数
        /// </summary>
        public Package<float[]> WriteFloatArray(string address, float[] values)
        {
            return WriteFloatArraySync(address, values);
        }

        /// <summary>
        /// 同步写入多个双精度浮点数
        /// </summary>
        public Package<double[]> WriteDoubleArray(string address, double[] values)
        {
            return WriteDoubleArraySync(address, values);
        }

        #endregion

        #region 触发器管理

        /// <summary>
        /// 添加触发器
        /// </summary>
        /// <param name="config">触发器配置</param>
        /// <returns>触发器ID</returns>
        public string AddTrigger(TriggerConfig config)
        {
            ThrowIfDisposed();
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            if (string.IsNullOrWhiteSpace(config.Address))
                throw new ArgumentException("触发器地址不能为空", nameof(config));

            lock (_triggerLock)
            {
                _triggers.Add(config);
            }

            return config.Id;
        }

        /// <summary>
        /// 添加触发器
        /// </summary>
        /// <param name="config">触发器配置</param>
        /// <param name="callback">该触发器的独立回调，在独立线程中执行，不会阻塞其他触发器</param>
        /// <returns>触发器ID</returns>
        public string AddTrigger(TriggerConfig config, Action<TriggerEventArgs> callback)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            config.Callback = callback;
            return AddTrigger(config);
        }

        /// <summary>
        /// 添加触发器
        /// </summary>
        /// <param name="name">触发器名称</param>
        /// <param name="address">监控地址</param>
        /// <param name="dataType">数据类型</param>
        /// <param name="triggerValue">触发值</param>
        /// <param name="condition">触发条件</param>
        /// <param name="functionCode">功能码</param>
        /// <returns>触发器ID</returns>
        public string AddTrigger(string name, string address, TriggerDataType dataType, object triggerValue,
            TriggerCondition condition = TriggerCondition.Equal, byte functionCode = 0x03)
        {
            var config = new TriggerConfig
            {
                Name = name,
                Address = address,
                DataType = dataType,
                TriggerValue = triggerValue?.ToString(),
                Condition = condition,
                FunctionCode = functionCode
            };

            return AddTrigger(config);
        }

        /// <summary>
        /// 添加触发器
        /// </summary>
        /// <param name="name">触发器名称</param>
        /// <param name="address">监控地址</param>
        /// <param name="dataType">数据类型</param>
        /// <param name="triggerValue">触发值</param>
        /// <param name="callback">该触发器的独立回调，在独立线程中执行，不会阻塞其他触发器</param>
        /// <param name="condition">触发条件</param>
        /// <param name="functionCode">功能码</param>
        /// <returns>触发器ID</returns>
        public string AddTrigger(string name, string address, TriggerDataType dataType, object triggerValue,
            Action<TriggerEventArgs> callback,
            TriggerCondition condition = TriggerCondition.Equal, byte functionCode = 0x03)
        {
            var config = new TriggerConfig
            {
                Name = name,
                Address = address,
                DataType = dataType,
                TriggerValue = triggerValue?.ToString(),
                Condition = condition,
                FunctionCode = functionCode,
                Callback = callback
            };

            return AddTrigger(config);
        }

        /// <summary>
        /// 移除触发器
        /// </summary>
        /// <param name="triggerId">触发器ID</param>
        /// <returns>是否移除成功</returns>
        public bool RemoveTrigger(string triggerId)
        {
            lock (_triggerLock)
            {
                var trigger = _triggers.FirstOrDefault(t => t.Id == triggerId);
                if (trigger != null)
                {
                    return _triggers.Remove(trigger);
                }
            }
            return false;
        }

        /// <summary>
        /// 移除所有触发器
        /// </summary>
        public void ClearTriggers()
        {
            lock (_triggerLock)
            {
                _triggers.Clear();
            }
        }

        /// <summary>
        /// 获取所有触发器配置
        /// </summary>
        public List<TriggerConfig> GetTriggers()
        {
            lock (_triggerLock)
            {
                return _triggers.ToList();
            }
        }

        /// <summary>
        /// 启用/禁用触发器
        /// </summary>
        public bool SetTriggerEnabled(string triggerId, bool enabled)
        {
            lock (_triggerLock)
            {
                var trigger = _triggers.FirstOrDefault(t => t.Id == triggerId);
                if (trigger != null)
                {
                    trigger.IsEnabled = enabled;
                    trigger.HasTriggered = false; // 重置触发
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 重置触发器状态
        /// </summary>
        public void ResetTrigger(string triggerId)
        {
            lock (_triggerLock)
            {
                var trigger = _triggers.FirstOrDefault(t => t.Id == triggerId);
                if (trigger != null)
                {
                    trigger.HasTriggered = false;
                    trigger.LastValue = null;
                }
            }
        }

        /// <summary>
        /// 重置所有触发器状态
        /// </summary>
        public void ResetAllTriggers()
        {
            lock (_triggerLock)
            {
                foreach (var trigger in _triggers)
                {
                    trigger.HasTriggered = false;
                    trigger.LastValue = null;
                }
            }
        }

        #endregion

        #region 触发器监控

        /// <summary>
        /// 启动触发器监控
        /// </summary>
        public void StartTriggerMonitor()
        {
            lock (_triggerLock)
            {
                if (_isTriggerMonitorRunning)
                    return;

                _triggerCancellationTokenSource = new CancellationTokenSource();
                _isTriggerMonitorRunning = true;
                _triggerMonitorTask = Task.Run(() => TriggerMonitorLoopAsync(_triggerCancellationTokenSource.Token));
            }
        }

        /// <summary>
        /// 停止触发器监控
        /// </summary>
        public async Task StopTriggerMonitorAsync()
        {
            Task monitorTask;
            CancellationTokenSource cts;

            lock (_triggerLock)
            {
                if (!_isTriggerMonitorRunning)
                    return;

                _isTriggerMonitorRunning = false;
                monitorTask = _triggerMonitorTask;
                cts = _triggerCancellationTokenSource;
                _triggerMonitorTask = null;
                _triggerCancellationTokenSource = null;
            }

            cts?.Cancel();

            if (monitorTask != null)
            {
                try
                {
                    await monitorTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }

            cts?.Dispose();
        }

        /// <summary>
        /// 同步停止触发器监控
        /// </summary>
        public void StopTriggerMonitor()
        {
            StopTriggerMonitorAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// 触发器监控循环
        /// </summary>
        private async Task TriggerMonitorLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && !_isDisposed)
            {
                try
                {
                    if (!IsConnected)
                    {
                        await Task.Delay(TriggerPollInterval, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    List<TriggerConfig> triggersToCheck;
                    lock (_triggerLock)
                    {
                        triggersToCheck = _triggers.Where(t => t.IsEnabled).ToList();
                    }

                    foreach (var trigger in triggersToCheck)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            break;

                        await CheckTriggerAsync(trigger).ConfigureAwait(false);
                    }
                    await Task.Delay(TriggerPollInterval, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Trigger monitor error: {ex.Message}");
                    await Task.Delay(TriggerPollInterval, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// 检查单个触发器
        /// </summary>
        private async Task CheckTriggerAsync(TriggerConfig trigger)
        {
            object currentValue = null;
            bool isSuccess = false;
            string errorMessage = null;
            try
            {
                switch (trigger.DataType)
                {
                    case TriggerDataType.Bit:
                        var bitResult = await ReadBitASync(trigger.Address, trigger.FunctionCode).ConfigureAwait(false);
                        isSuccess = bitResult.IsSuccess;
                        errorMessage = bitResult.ErrorMessage;
                        if (isSuccess) currentValue = bitResult.Value;
                        break;

                    case TriggerDataType.Bool:
                        var boolResult = await ReadBoolAsync(trigger.Address, trigger.FunctionCode).ConfigureAwait(false);
                        isSuccess = boolResult.IsSuccess;
                        errorMessage = boolResult.ErrorMessage;
                        if (isSuccess) currentValue = boolResult.Value;
                        break;

                    case TriggerDataType.Int16:
                        var int16Result = await ReadInt16Async(trigger.Address, trigger.FunctionCode).ConfigureAwait(false);
                        isSuccess = int16Result.IsSuccess;
                        errorMessage = int16Result.ErrorMessage;
                        if (isSuccess) currentValue = int16Result.Value;
                        break;

                    case TriggerDataType.UInt16:
                        var uint16Result = await ReadUInt16Async(trigger.Address, trigger.FunctionCode).ConfigureAwait(false);
                        isSuccess = uint16Result.IsSuccess;
                        errorMessage = uint16Result.ErrorMessage;
                        if (isSuccess) currentValue = uint16Result.Value;
                        break;

                    case TriggerDataType.Int32:
                        var int32Result = await ReadInt32Async(trigger.Address, trigger.FunctionCode).ConfigureAwait(false);
                        isSuccess = int32Result.IsSuccess;
                        errorMessage = int32Result.ErrorMessage;
                        if (isSuccess) currentValue = int32Result.Value;
                        break;

                    case TriggerDataType.UInt32:
                        var uint32Result = await ReadUInt32Async(trigger.Address, trigger.FunctionCode).ConfigureAwait(false);
                        isSuccess = uint32Result.IsSuccess;
                        errorMessage = uint32Result.ErrorMessage;
                        if (isSuccess) currentValue = uint32Result.Value;
                        break;

                    case TriggerDataType.Int64:
                        var int64Result = await ReadInt64Async(trigger.Address, trigger.FunctionCode).ConfigureAwait(false);
                        isSuccess = int64Result.IsSuccess;
                        errorMessage = int64Result.ErrorMessage;
                        if (isSuccess) currentValue = int64Result.Value;
                        break;

                    case TriggerDataType.Float:
                        var floatResult = await ReadFloatAsync(trigger.Address, trigger.FunctionCode).ConfigureAwait(false);
                        isSuccess = floatResult.IsSuccess;
                        errorMessage = floatResult.ErrorMessage;
                        if (isSuccess) currentValue = floatResult.Value;
                        break;

                    case TriggerDataType.Double:
                        var doubleResult = await ReadDoubleAsync(trigger.Address, trigger.FunctionCode).ConfigureAwait(false);
                        isSuccess = doubleResult.IsSuccess;
                        errorMessage = doubleResult.ErrorMessage;
                        if (isSuccess) currentValue = doubleResult.Value;
                        break;

                    case TriggerDataType.String:
                        var stringResult = await ReadStringAsync(trigger.Address, trigger.TriggerValueLength, trigger.FunctionCode).ConfigureAwait(false);
                        isSuccess = stringResult.IsSuccess;
                        errorMessage = stringResult.ErrorMessage;
                        if (isSuccess) currentValue = stringResult.Value;
                        break;
                }
                if (!isSuccess)
                {
                    if (IsConnected)
                    {
                        OnTriggerEvent(new TriggerEventArgs
                        {
                            Trigger = trigger,
                            CurrentValue = null,
                            PreviousValue = trigger.LastValue,
                            TriggerTime = DateTime.Now,
                            IsSuccess = false,
                            ErrorMessage = errorMessage ?? "读取失败"
                        });
                    }
                    return;
                }
                bool shouldTrigger = CheckTriggerCondition(trigger, currentValue);

                if (shouldTrigger && !trigger.HasTriggered)
                {
                    trigger.HasTriggered = true;
                    List<LinkedReadResult> linkedValues = null;
                    if (trigger.LinkedReads?.Count > 0)
                    {
                        linkedValues = new List<LinkedReadResult>();
                        var snapshot = trigger.LinkedReads.ToList();
                        foreach (var link in snapshot)
                        {
                            linkedValues.Add(await ReadLinkedValueAsync(link).ConfigureAwait(false));
                        }
                    }

                    OnTriggerEvent(new TriggerEventArgs
                    {
                        Trigger = trigger,
                        CurrentValue = currentValue,
                        PreviousValue = trigger.LastValue,
                        TriggerTime = DateTime.Now,
                        IsSuccess = true,
                        LinkedValues = linkedValues
                    });
                }
                else if (!shouldTrigger)
                {
                    trigger.HasTriggered = false;
                }

                trigger.LastValue = currentValue;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Check trigger error: {ex.Message}");
            }
        }

        /// <summary>
        /// 按数据类型读取一个连带地址
        /// </summary>
        private async Task<LinkedReadResult> ReadLinkedValueAsync(LinkedReadConfig link)
        {
            var result = new LinkedReadResult
            {
                Address = link.Address,
                Name = link.Name,
                DataType = link.DataType
            };

            try
            {
                switch (link.DataType)
                {
                    case TriggerDataType.Bit:
                        var bitR = await ReadBitASync(link.Address).ConfigureAwait(false);
                        result.IsSuccess = bitR.IsSuccess;
                        result.ErrorMessage = bitR.ErrorMessage;
                        if (bitR.IsSuccess) result.Value = bitR.Value;
                        break;

                    case TriggerDataType.Bool:
                        var boolR = await ReadBoolAsync(link.Address).ConfigureAwait(false);
                        result.IsSuccess = boolR.IsSuccess;
                        result.ErrorMessage = boolR.ErrorMessage;
                        if (boolR.IsSuccess) result.Value = boolR.Value;
                        break;

                    case TriggerDataType.Int16:
                        var i16R = await ReadInt16Async(link.Address).ConfigureAwait(false);
                        result.IsSuccess = i16R.IsSuccess;
                        result.ErrorMessage = i16R.ErrorMessage;
                        if (i16R.IsSuccess) result.Value = i16R.Value;
                        break;

                    case TriggerDataType.UInt16:
                        var u16R = await ReadUInt16Async(link.Address).ConfigureAwait(false);
                        result.IsSuccess = u16R.IsSuccess;
                        result.ErrorMessage = u16R.ErrorMessage;
                        if (u16R.IsSuccess) result.Value = u16R.Value;
                        break;

                    case TriggerDataType.Int32:
                        var i32R = await ReadInt32Async(link.Address).ConfigureAwait(false);
                        result.IsSuccess = i32R.IsSuccess;
                        result.ErrorMessage = i32R.ErrorMessage;
                        if (i32R.IsSuccess) result.Value = i32R.Value;
                        break;

                    case TriggerDataType.UInt32:
                        var u32R = await ReadUInt32Async(link.Address).ConfigureAwait(false);
                        result.IsSuccess = u32R.IsSuccess;
                        result.ErrorMessage = u32R.ErrorMessage;
                        if (u32R.IsSuccess) result.Value = u32R.Value;
                        break;

                    case TriggerDataType.Int64:
                        var i64R = await ReadInt64Async(link.Address).ConfigureAwait(false);
                        result.IsSuccess = i64R.IsSuccess;
                        result.ErrorMessage = i64R.ErrorMessage;
                        if (i64R.IsSuccess) result.Value = i64R.Value;
                        break;

                    case TriggerDataType.Float:
                        var fR = await ReadFloatAsync(link.Address).ConfigureAwait(false);
                        result.IsSuccess = fR.IsSuccess;
                        result.ErrorMessage = fR.ErrorMessage;
                        if (fR.IsSuccess) result.Value = fR.Value;
                        break;

                    case TriggerDataType.Double:
                        var dR = await ReadDoubleAsync(link.Address).ConfigureAwait(false);
                        result.IsSuccess = dR.IsSuccess;
                        result.ErrorMessage = dR.ErrorMessage;
                        if (dR.IsSuccess) result.Value = dR.Value;
                        break;

                    case TriggerDataType.String:
                        var sR = await ReadStringAsync(link.Address, link.Length).ConfigureAwait(false);
                        result.IsSuccess = sR.IsSuccess;
                        result.ErrorMessage = sR.ErrorMessage;
                        if (sR.IsSuccess) result.Value = sR.Value;
                        break;

                    default:
                        result.IsSuccess = false;
                        result.ErrorMessage = $"不支持的数据类型: {link.DataType}";
                        break;
                }
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// 检查触发条件是否满足
        /// </summary>
        private bool CheckTriggerCondition(TriggerConfig trigger, object currentValue)
        {
            if (currentValue == null)
                return false;
            if (trigger.Condition == TriggerCondition.Changed)
            {
                if (trigger.LastValue == null)
                    return false;

                return !Equals(currentValue, trigger.LastValue);
            }
            object triggerValue = ParseTriggerValue(trigger.TriggerValue, trigger.DataType);
            if (triggerValue == null)
                return false;
            int comparison = CompareValues(currentValue, triggerValue, trigger.DataType);

            switch (trigger.Condition)
            {
                case TriggerCondition.Equal: return comparison == 0;
                case TriggerCondition.NotEqual: return comparison != 0;
                case TriggerCondition.GreaterThan: return comparison > 0;
                case TriggerCondition.LessThan: return comparison < 0;
                case TriggerCondition.GreaterThanOrEqual: return comparison >= 0;
                case TriggerCondition.LessThanOrEqual: return comparison <= 0;
                default: return false;
            }
        }

        /// <summary>
        /// 解析触发值
        /// </summary>
        private object ParseTriggerValue(string value, TriggerDataType dataType)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            try
            {
                switch (dataType)
                {
                    case TriggerDataType.Bit: return bool.Parse(value);
                    case TriggerDataType.Bool: return bool.Parse(value);
                    case TriggerDataType.Int16: return short.Parse(value);
                    case TriggerDataType.UInt16: return ushort.Parse(value);
                    case TriggerDataType.Int32: return int.Parse(value);
                    case TriggerDataType.UInt32: return uint.Parse(value);
                    case TriggerDataType.Int64: return long.Parse(value);
                    case TriggerDataType.Float: return float.Parse(value);
                    case TriggerDataType.Double: return double.Parse(value);
                    case TriggerDataType.String: return value;
                    default: return null;
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 比较两个值
        /// </summary>
        private int CompareValues(object currentValue, object triggerValue, TriggerDataType dataType)
        {
            switch (dataType)
            {
                case TriggerDataType.Bit: return ((bool)currentValue).CompareTo((bool)triggerValue);
                case TriggerDataType.Bool: return ((bool)currentValue).CompareTo((bool)triggerValue);
                case TriggerDataType.Int16: return ((short)currentValue).CompareTo((short)triggerValue);
                case TriggerDataType.UInt16: return ((ushort)currentValue).CompareTo((ushort)triggerValue);
                case TriggerDataType.Int32: return ((int)currentValue).CompareTo((int)triggerValue);
                case TriggerDataType.UInt32: return ((uint)currentValue).CompareTo((uint)triggerValue);
                case TriggerDataType.Int64: return ((long)currentValue).CompareTo((long)triggerValue);
                case TriggerDataType.Float: return ((float)currentValue).CompareTo((float)triggerValue);
                case TriggerDataType.Double: return ((double)currentValue).CompareTo((double)triggerValue);
                case TriggerDataType.String: return ((string)currentValue).CompareTo((string)triggerValue);
                default: return 0;
            }
        }

        /// <summary>
        /// 触发事件
        /// </summary>
        private void OnTriggerEvent(TriggerEventArgs e)
        {
            if (_isDisposed) return;

            e.Client = this;
            var trigger = e.Trigger;
            var callback = trigger?.Callback;
            var thenAction = trigger?.ThenAction;
            if (callback != null || thenAction != null)
            {
                Task.Run(async () =>
                {
                    // 回调
                    if (!_isDisposed && callback != null)
                    {
                        try
                        {
                            callback(e);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Trigger [{trigger?.Name}] callback error: {ex.Message}");
                        }
                    }

                    // Then
                    if (!_isDisposed && thenAction != null)
                    {
                        try
                        {
                            if (ThenDelayMs > 0)
                                await Task.Delay(ThenDelayMs).ConfigureAwait(false);
                            if (!_isDisposed)
                                thenAction(this);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Trigger [{trigger?.Name}] Then error: {ex.Message}");
                        }
                    }
                });
            }

            // 全局事件
            var handler = TriggerEvent;
            if (handler != null)
            {
                Task.Run(() =>
                {
                    if (_isDisposed) return;
                    try
                    {
                        handler(this, e);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"TriggerEvent handler error: {ex.Message}");
                    }
                });
            }
        }

        #endregion

        #region IDisposable 实现

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _userRequestedDisconnect = true;

            if (disposing)
            {
                try
                {
                    StopTriggerMonitorAsync().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error stopping trigger monitor: {ex.Message}");
                }

                // 停止心跳检测
                StopHeartbeat();

                // 取消重连
                try
                {
                    _reconnectCts?.Cancel();
                    _reconnectCts?.Dispose();
                    _reconnectCts = null;
                }
                catch { }

                _cancellationTokenSource?.Cancel();

                try
                {
                    DisconnectInternalAsync().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error during disposal: {ex.Message}");
                }

                _sendLock?.Dispose();
                _cancellationTokenSource?.Dispose();
                _triggerCancellationTokenSource?.Dispose();
            }
        }

        /// <summary>
        /// 析构函数
        /// </summary>
        ~ModbusTCPClientPlus()
        {
            Dispose(false);
        }

        #endregion
    }
}