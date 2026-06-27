# ModbusTCPClientPlus

一个功能完整的 Modbus TCP 客户端库，支持同步/异步读写、触发器监控、自动重连、心跳检测。

- **.NET Framework 4.5+** / **.NET Standard 2.0+**
- 依赖：无第三方库，仅 `System.Net.Sockets`

---

## 目录

- [快速开始](#快速开始)
- [连接管理](#连接管理)
- [读写操作](#读写操作)
- [Package\<T\> 返回值](#packaget-返回值)
- [触发器系统](#触发器系统)
- [心跳检测](#心跳检测)
- [事件](#事件)
- [配置参考](#配置参考)
- [字节序](#字节序)
- [枚举定义](#枚举定义)
- [释放资源](#释放资源)
- [完整示例](#完整示例)

---

## 快速开始

```csharp
using ModbusIntegration.Modbus;

// 创建客户端并连接到服务器
var client = new ModbusTCPClientPlus("192.168.1.100", 502);

// 读取一个 16 位整数（保持寄存器）
var result = await client.ReadInt16Async("100");
if (result.IsSuccess)
    Console.WriteLine($"值: {result.Value}");

// 写入一个浮点数
await client.WriteFloatAsync("200", 36.5f);

// 用完释放
client.Dispose();
```

---

## 连接管理

### 创建并连接

```csharp
// 方式 1：构造时自动连接（字节序默认 CDAB）
var client = new ModbusTCPClientPlus("192.168.1.100", 502);

// 方式 2：指定字节序
var client = new ModbusTCPClientPlus("192.168.1.100", 502, ByteOrder.ABCD);

// 方式 3：先创建，再连接
var client = new ModbusTCPClientPlus();
client.StationNumber = 1;
client.AddressStartsAtOne = true;
client.AsyncNewTcp("192.168.1.100", 502);
```

### 自动重连

连接断开后自动重连，默认启用。

```csharp
client.AutoReconnect = true;              // 默认: true
client.ReconnectInterval = 3000;          // 重连间隔 (ms)，默认: 3000
client.MaxReconnectAttempts = 0;          // 0 = 无限重连，默认: 0
client.ConnectionTimeout = 5000;          // 连接超时 (ms)，默认: 5000
```

### 断开连接

```csharp
await client.DisconnectAsync();    // 异步
client.Disconnect();               // 同步
```

---

## 读写操作

### 异步读取（推荐）

返回 `Task<Package<T>>`，不阻塞线程。

| 方法 | 返回类型 | 说明 |
|---|---|---|
| `ReadBoolAsync(addr)` | `Package<bool>` | 读线圈 (FC=0x01) |
| `ReadBoolAsync(addr, 0x02)` | `Package<bool>` | 读离散输入 (FC=0x02) |
| `ReadBoolArrayAsync(addr, count)` | `Package<bool[]>` | 读多个线圈 |
| `ReadInt16Async(addr)` | `Package<short>` | 读 16 位有符号整数 |
| `ReadUInt16Async(addr)` | `Package<ushort>` | 读 16 位无符号整数 |
| `ReadInt32Async(addr)` | `Package<int>` | 读 32 位有符号整数 |
| `ReadUInt32Async(addr)` | `Package<uint>` | 读 32 位无符号整数 |
| `ReadInt64Async(addr)` | `Package<long>` | 读 64 位有符号整数 |
| `ReadFloatAsync(addr)` | `Package<float>` | 读 32 位浮点数 |
| `ReadDoubleAsync(addr)` | `Package<double>` | 读 64 位浮点数 |
| `ReadStringAsync(addr, length)` | `Package<string>` | 读 ASCII 字符串 |
| `ReadInt16ArrayAsync(addr, count)` | `Package<short[]>` | 读多个 16 位整数 |
| `ReadFloatArrayAsync(addr, count)` | `Package<float[]>` | 读多个浮点数 |

```csharp
// 基本用法
var r = await client.ReadFloatAsync("300");
if (r.IsSuccess)
    Console.WriteLine($"温度: {r.Value} °C");

// 指定功能码（0x03=保持寄存器, 0x04=输入寄存器）
var r2 = await client.ReadInt32Async("200", 0x04);

// 读取数组
var r3 = await client.ReadFloatArrayAsync("400", 10); // 10 个浮点数
```

### 异步写入

| 方法 | 说明 |
|---|---|
| `WriteBoolAsync(addr, value)` | 写单个线圈 (FC=0x05) |
| `WriteBoolArrayAsync(addr, values)` | 写多个线圈 (FC=0x0F) |
| `WriteInt16Async(addr, value)` | 写 16 位整数 (FC=0x06) |
| `WriteFloatAsync(addr, value)` | 写 32 位浮点数 (FC=0x10) |
| `WriteInt32Async(addr, value)` | 写 32 位整数 (FC=0x10) |
| `WriteDoubleAsync(addr, value)` | 写 64 位浮点数 (FC=0x10) |
| `WriteStringAsync(addr, value, maxLen)` | 写 ASCII 字符串 (FC=0x10) |
| ... | *(支持所有数据类型的同步/异步写入)* |

```csharp
var w = await client.WriteFloatAsync("300", 25.0f);
if (w.IsSuccess)
    Console.WriteLine("写入成功");
```

### 同步读写

所有异步方法都有对应的同步版本（去掉 `Async` 后缀），返回 `Package<T>` 而非 `Task<Package<T>>`。

```csharp
var r = client.ReadInt16("100");   // 同步，阻塞当前线程
var w = client.WriteFloat("300", 12.5f);
```

> **建议：** UI 应用使用异步方法，控制台/后台线程可使用同步方法。

---

## Package\<T\> 返回值

所有读写方法都返回 `Package<T>`，包含操作结果和诊断信息。

```csharp
public class Package<T>
{
    public T Value { get; set; }              // 读取/写入的值
    public bool IsSuccess { get; set; }       // 操作是否成功
    public string ErrorMessage { get; set; }  // 失败时的错误信息
    public TimeSpan ElapsedTime { get; set; } // 操作耗时
    public byte FunctionCode { get; set; }    // 响应的功能码
    public byte ExceptionCode { get; set; }   // Modbus 异常码
    public string Address { get; set; }       // 起始地址

    // 调试方法
    public string GetSendHex()                // 发送报文的十六进制
    public string GetReceiveHex()             // 接收报文的十六进制
    public override string ToString()         // 简要结果描述
}
```

### 错误处理

```csharp
var r = await client.ReadInt32Async("200");
if (!r.IsSuccess)
{
    Console.WriteLine($"读取失败: {r.ErrorMessage}");
    // ErrorMessage 示例:
    //   "未连接到服务器"
    //   "读取超时(5000ms)"
    //   "非法数据地址"      (Modbus 异常码 0x02)
    //   "服务器设备忙"      (Modbus 异常码 0x06)
}
```

---

## 触发器系统

触发器自动轮询指定的寄存器地址，当值满足条件时执行回调。

### 基本用法

```csharp
// 添加触发器：当地址 300 的值等于 80 时触发
client.AddTrigger(new TriggerConfig()
{
    Address = "300",
    DataType = TriggerDataType.Float,
    Condition = TriggerCondition.Equal,
    TriggerValue = "80",
    Callback = new Action<TriggerEventArgs>(async (e) =>
    {
        Console.WriteLine($"触发! 当前值={e.CurrentValue}");
    })
});

// 启动监控（必须在添加触发器之后调用）
client.StartTriggerMonitor();
```

### 触发条件

```csharp
public enum TriggerCondition
{
    Equal,              // == 触发值
    NotEqual,           // != 触发值
    GreaterThan,        // >  触发值
    LessThan,           // <  触发值
    GreaterThanOrEqual, // >= 触发值
    LessThanOrEqual,    // <= 触发值
    Changed,            // 值发生变化即触发
}
```

### 回调参数 TriggerEventArgs

```csharp
public class TriggerEventArgs : EventArgs
{
    public ModbusTCPClientPlus Client { get; }  // 客户端实例，可用来做后续读写
    public TriggerConfig Trigger { get; }       // 触发的配置
    public object CurrentValue { get; }         // 当前值
    public object PreviousValue { get; }        // 上一次的值
    public DateTime TriggerTime { get; }        // 触发时间
    public bool IsSuccess { get; }              // 读取是否成功
    public string ErrorMessage { get; }         // 错误信息
    public List<LinkedReadResult> LinkedValues; // 连带读取结果
}
```

### 触发后执行操作：`.Then()`

Callback 执行完成后自动调用，常用于复位触发地址。

```csharp
new TriggerConfig()
{
    Address = "300",
    DataType = TriggerDataType.Float,
    Condition = TriggerCondition.GreaterThan,
    TriggerValue = "80",
    Callback = new Action<TriggerEventArgs>((e) =>
    {
        Console.WriteLine($"告警: 温度={e.CurrentValue}");
    })
}.Then(client =>
{
    // 回调完成后执行复位
    client.WriteFloat("300", 0.0f);     // 触发地址归零
    client.WriteBool("500", false);     // 复位告警标志
});

client.ThenDelayMs = 50;  // Callback 完成后等待 50ms 再执行 Then，默认 0
```

### 连带读取：`.Link()`

触发时自动读取其他关联寄存器，结果通过 `e.GetLinkedValue<T>()` 获取。

```csharp
new TriggerConfig()
{
    Address = "100",
    DataType = TriggerDataType.Int16,
    Condition = TriggerCondition.Equal,
    TriggerValue = "1",
    Callback = new Action<TriggerEventArgs>((e) =>
    {
        // 用强类型方法直接取值
        int    count    = e.GetLinkedValue<int>("产量");
        float  pressure = e.GetLinkedValue<float>("压力");
        bool   running  = e.GetLinkedValue<bool>("运行");

        // 或按地址取值
        float temp = e.GetLinkedValue<float>("400");

        // 安全取值
        if (e.TryGetLinkedValue<int>("产量", out int c))
            Console.WriteLine($"产量: {c}");
    })
}
    .Link("200", TriggerDataType.Int32, "产量")   // 名称=产量
    .Link("300", TriggerDataType.Float, "压力")   // 名称=压力
    .Link("500", TriggerDataType.Bool, "运行")    // 名称=运行
    .Then(client =>
    {
        client.WriteInt16("100", 0);  // 复位触发地址
    });
```

### 触发器管理

```csharp
// 添加触发器
string id = client.AddTrigger(config);

// 移除/清空
bool ok = client.RemoveTrigger(id);
client.ClearTriggers();

// 启用/禁用
client.SetTriggerEnabled(id, false);

// 重置状态（允许再次触发）
client.ResetTrigger(id);
client.ResetAllTriggers();

// 获取所有配置
List<TriggerConfig> all = client.GetTriggers();
int count = client.TriggerCount;
```

### 监控控制

```csharp
client.StartTriggerMonitor();          // 启动
await client.StopTriggerMonitorAsync(); // 异步停止
client.StopTriggerMonitor();           // 同步停止
bool running = client.IsTriggerMonitorRunning;
```

---

## 心跳检测

心跳通过**读 → 写反转**的方式验证双向通信。每隔 `HeartbeatInterval` 读取心跳地址的当前值，然后写入反转值（0→1, 1→0）。任一步失败即判定连接断开并触发重连。

```csharp
client.EnableHeartbeat = true;         // 启用心跳，默认: true
client.HeartbeatAddress = "0";         // 心跳占用地址，默认: "0"
client.HeartbeatFunctionCode = 0x03;   // 功能码 0x03（保持寄存器），默认: 0x03
client.HeartbeatInterval = 1000;       // 心跳间隔 (ms)，默认: 1000
```

> **注意：** 心跳地址会持续 0↔1 翻转，**不可**用于存储业务数据。选用一个空闲地址。

---

## 事件

```csharp
// 连接成功
client.SuccessfulConnectEvent += (DateTime time, string ip, int port) =>
{
    Console.WriteLine($"[{time}] 已连接到 {ip}:{port}");
};

// 连接断开
client.DisconnectionEvent += (DateTime time, string ip, int port, Exception ex) =>
{
    Console.WriteLine($"[{time}] 断开连接: {ex?.Message}");
};

// 每次读写操作（可用于日志/调试）
client.InteractionEvent += (TimeSpan elapsed, bool success, string method,
                            string address, int count, string value) =>
{
    Console.WriteLine($"[{elapsed.TotalMilliseconds:F0}ms] {method} {address}" +
                      $" x{count} = {value} ({(success ? "OK" : "FAIL")})");
};

// 全局触发器事件（所有触发器共用）
client.TriggerEvent += (sender, e) =>
{
    Console.WriteLine($"触发: {e.Trigger?.Name} = {e.CurrentValue}");
};
```

---

## 配置参考

### ModbusTCPClientPlus 属性

| 属性 | 类型 | 默认值 | 说明 |
|---|---|---|---|
| `TargetIP` | `string` | -- | 服务器 IP（只读） |
| `TargetPort` | `int` | -- | 端口号（只读） |
| `IsConnected` | `bool` | -- | 是否已连接（只读） |
| `StationNumber` | `ushort` | `1` | Modbus 站号 |
| `AddressStartsAtOne` | `bool` | `false` | `true`=地址从 1 开始 |
| `Order` | `ByteOrder` | `CDAB` | 字节序 |
| `ReverseString` | `bool` | `false` | `true`=读写字符串时颠倒字节序 |
| `ReadTimeout` | `int` | `5000` | 读写超时 (ms) |
| `ConnectionTimeout` | `int` | `5000` | 连接超时 (ms) |
| `AutoReconnect` | `bool` | `true` | 是否自动重连 |
| `ReconnectInterval` | `int` | `3000` | 重连间隔 (ms) |
| `MaxReconnectAttempts` | `int` | `0` | 最大重连次数，`0`=无限 |
| `EnableHeartbeat` | `bool` | `true` | 启用心跳 |
| `HeartbeatAddress` | `string` | `"0"` | 心跳地址 |
| `HeartbeatFunctionCode` | `byte` | `0x03` | 心跳功能码 |
| `HeartbeatInterval` | `int` | `1000` | 心跳间隔 (ms) |
| `TriggerPollInterval` | `int` | `100` | 触发器轮询间隔 (ms) |
| `ThenDelayMs` | `int` | `0` | Callback 完成后到 Then 的延迟 (ms) |

---

## 字节序

不同 PLC 厂商使用不同的字节序。通过 `Order` 属性设置：

```csharp
client.Order = ByteOrder.CDAB;  // 默认，适用于 Mitsubishi、Omron 等
```

| 枚举值 | 线序 | 说明 | 常见设备 |
|---|---|---|---|
| `ABCD` | [01,02,03,04] | 大端序，高字在前 | Siemens、标准 Modbus |
| `BADC` | [02,01,04,03] | 字节交换，高字在前 | 某些 RTU |
| `CDAB` | [03,04,01,02] | 字交换，低字在前 | **默认**，Mitsubishi、Omron |
| `DCBA` | [04,03,02,01] | 小端序，低字在前 | 部分国产 PLC |

---

## 枚举定义

### TriggerDataType — 触发器支持的数据类型

```csharp
Bit, Bool, Int16, UInt16, Int32, UInt32, Int64, Float, Double, String
```

### Modbus 功能码

```csharp
ModbusTCPClientPlus.FunctionCode.ReadCoils              // 0x01
ModbusTCPClientPlus.FunctionCode.ReadDiscreteInputs     // 0x02
ModbusTCPClientPlus.FunctionCode.ReadHoldingRegisters   // 0x03
ModbusTCPClientPlus.FunctionCode.ReadInputRegisters     // 0x04
ModbusTCPClientPlus.FunctionCode.WriteSingleCoil         // 0x05
ModbusTCPClientPlus.FunctionCode.WriteSingleRegister     // 0x06
ModbusTCPClientPlus.FunctionCode.WriteMultipleCoils      // 0x0F
ModbusTCPClientPlus.FunctionCode.WriteMultipleRegisters  // 0x10
```

### Modbus 异常码

```csharp
string desc = ModbusTCPClientPlus.ExceptionCode.GetDescription(0x02);
// "非法数据地址"
```

---

## 释放资源

用完后必须调用 `Dispose()` 释放网络资源和后台任务。

```csharp
// 方式 1：手动释放
client.Dispose();

// 方式 2：using 语句
using (var client = new ModbusTCPClientPlus("192.168.1.100", 502))
{
    var r = await client.ReadFloatAsync("100");
}
```

`Dispose()` 会自动：停止触发器监控 → 停止心跳 → 取消重连 → 关闭 TCP 连接 → 释放锁和令牌。

---

## 完整示例

### WinForms 应用

```csharp
using ModbusIntegration.Modbus;
using static ModbusIntegration.Modbus.ModbusTCPClientPlus;

public partial class Form1 : Form
{
    ModbusTCPClientPlus client;

    public Form1()
    {
        InitializeComponent();

        client = new ModbusTCPClientPlus("192.168.1.100", 502);

        // 连接事件
        client.SuccessfulConnectEvent += (time, ip, port) =>
            Log($"已连接 {ip}:{port}");

        client.DisconnectionEvent += (time, ip, port, ex) =>
            Log($"断开: {ex?.Message}");

        // 配置触发器：温度超过 80°C 时告警
        client.AddTrigger(new TriggerConfig()
        {
            Name = "高温告警",
            Address = "300",
            DataType = TriggerDataType.Float,
            Condition = TriggerCondition.GreaterThan,
            TriggerValue = "80",
            Callback = new Action<TriggerEventArgs>(async (e) =>
            {
                // 读取关联的产量和压力数据
                int count = e.GetLinkedValue<int>("产量");
                float pressure = e.GetLinkedValue<float>("压力");

                Log($"⚠ 高温告警! 温度={e.CurrentValue}°C, " +
                    $"产量={count}, 压力={pressure}");

                // 如果需要确认告警，可以向 e.Client 写入
            })
        }
            .Link("200", TriggerDataType.Int32, "产量")
            .Link("400", TriggerDataType.Float, "压力")
            .Then(client =>
            {
                // 告警处理完成后复位告警标志
                client.WriteBool("500", false);
            }));

        client.StartTriggerMonitor();
    }

    private async void btnRead_Click(object sender, EventArgs e)
    {
        var r = await client.ReadFloatAsync("300");
        if (r.IsSuccess)
            lblTemp.Text = $"{r.Value} °C";
        else
            MessageBox.Show($"读取失败: {r.ErrorMessage}");
    }

    private async void btnWrite_Click(object sender, EventArgs e)
    {
        float val = float.Parse(txtValue.Text);
        var w = await client.WriteFloatAsync("300", val);
        Log(w.IsSuccess ? $"写入成功: {val}" : $"写入失败: {w.ErrorMessage}");
    }

    private void Log(string msg)
    {
        if (InvokeRequired)
            Invoke(new Action(() => richTextBox.AppendText($"{DateTime.Now} {msg}\n")));
        else
            richTextBox.AppendText($"{DateTime.Now} {msg}\n");
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        client?.Dispose();
        base.OnFormClosed(e);
    }
}
```

### 控制台轮询

```csharp
using var client = new ModbusTCPClientPlus("192.168.1.100", 502);

while (true)
{
    var temp = await client.ReadFloatAsync("300");
    var press = await client.ReadFloatAsync("400");
    var running = await client.ReadBoolAsync("500");

    Console.WriteLine($"温度={temp.Value:F1}°C  压力={press.Value:F2}  运行={running.Value}");

    await Task.Delay(1000);
}
```

---

## 常见问题

**Q: 读写返回 "未连接到服务器"？**
A: 检查 IP/端口是否正确，确认 `IsConnected` 为 `true`。启动时连接是异步的，可监听 `SuccessfulConnectEvent` 确认连接成功。

**Q: 读取的值与实际不符？**
A: 调整 `Order` 字节序。最常见的问题——尝试换成 `ABCD` 或 `DCBA`。

**Q: 地址从 0 还是 1 开始？**
A: 默认从 0 开始，设置 `client.AddressStartsAtOne = true` 改为从 1 开始。

**Q: 触发器不触发？**
A: 确认已调用 `StartTriggerMonitor()`，且 `IsEnabled = true`。检查 `Condition` 和 `TriggerValue` 是否配置正确。

**Q: 心跳地址被翻转了怎么办？**
A: 心跳地址会持续 0↔1 翻转，这是正常现象。确保心跳地址**不与业务数据地址冲突**。
