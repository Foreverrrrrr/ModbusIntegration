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

// 方式 3：先创建默认实例，再配置属性后连接
var client = new ModbusTCPClientPlus();
client.StationNumber = 1;
client.AddressStartsAtOne = true;
client.Order = ByteOrder.DCBA;
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

触发器自动轮询指定的寄存器地址，当值满足条件时执行回调。每个触发器在独立的线程池任务中执行，不会阻塞监控循环。

### 添加触发器的 5 种方式

**方式 1：使用 `TriggerConfig` 对象 + Lambda 回调（最灵活，推荐）**

```csharp
var config = new TriggerConfig()
{
    Address = "300",
    DataType = TriggerDataType.Float,
    Condition = TriggerCondition.Equal,
    TriggerValue = "80",
    Callback = new Action<TriggerEventArgs>((e) =>
    {
        Console.WriteLine($"触发! 当前值={e.CurrentValue}");
    })
};
string id = client.AddTrigger(config);
```

**方式 2：使用传统命名方法回调**

```csharp
// 定义一个独立的方法作为回调
private void OnHighTemperature(TriggerEventArgs e)
{
    var client = e.Client;
    Console.WriteLine($"[{e.TriggerTime}] 高温告警!");
    Console.WriteLine($"  触发地址: {e.Trigger?.Address}");
    Console.WriteLine($"  当前值: {e.CurrentValue}");
    Console.WriteLine($"  上一次值: {e.PreviousValue}");

    // 在回调中通过 e.Client 执行后续操作
    if (e.IsSuccess && client.IsConnected)
    {
        // 可以在这里进行任意的读写
    }
}

// 添加触发器时引用该方法
var config = new TriggerConfig()
{
    Address = "300",
    DataType = TriggerDataType.Float,
    Condition = TriggerCondition.GreaterThan,
    TriggerValue = "80",
    Callback = new Action<TriggerEventArgs>(OnHighTemperature)  // 传方法引用
};
client.AddTrigger(config);
```

**方式 3：使用简化的 `AddTrigger` 重载（无需手动创建 `TriggerConfig`）**

```csharp
// 不传 Callback 的重载
string id = client.AddTrigger(
    name: "温度监控",
    address: "300",
    dataType: TriggerDataType.Float,
    triggerValue: "80",
    condition: TriggerCondition.GreaterThan,
    functionCode: 0x03
);

// 传 Callback 的重载
string id2 = client.AddTrigger(
    name: "压力监控",
    address: "400",
    dataType: TriggerDataType.Float,
    triggerValue: "100",
    callback: new Action<TriggerEventArgs>(OnPressureAlert),
    condition: TriggerCondition.GreaterThanOrEqual,
    functionCode: 0x03
);
```

**方式 4：`AddTrigger` 重载中直接传回调 Lambda**

```csharp
client.AddTrigger(
    name: "液位监控",
    address: "500",
    dataType: TriggerDataType.Int16,
    triggerValue: "50",
    callback: new Action<TriggerEventArgs>((e) =>
    {
        Console.WriteLine($"液位达到: {e.CurrentValue}");
    }),
    condition: TriggerCondition.LessThan
);
```

**方式 5：将 `Action<TriggerEventArgs>` 声明为变量**

```csharp
// 适合多个触发器共享相同回调逻辑
Action<TriggerEventArgs> sharedCallback = (e) =>
{
    Console.WriteLine($"触发器 [{e.Trigger?.Name}] 触发, 值={e.CurrentValue}");
};

var trigger1 = new TriggerConfig()
{
    Name = "触发器1",
    Address = "100",
    DataType = TriggerDataType.Int16,
    Condition = TriggerCondition.Equal,
    TriggerValue = "1",
    Callback = sharedCallback
};

var trigger2 = new TriggerConfig()
{
    Name = "触发器2",
    Address = "200",
    DataType = TriggerDataType.Int16,
    Condition = TriggerCondition.Equal,
    TriggerValue = "1",
    Callback = sharedCallback
};

client.AddTrigger(trigger1);
client.AddTrigger(trigger2);
```

> **注意：** 添加触发器后必须调用 `client.StartTriggerMonitor()` 才会开始轮询。

---

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
    Changed,            // 值发生变化即触发（无需设置 TriggerValue）
}
```

**条件使用示例：**

```csharp
// 相等触发：当地址 100 的 Int16 值等于 1 时触发
new TriggerConfig()
{
    Address = "100",
    DataType = TriggerDataType.Int16,
    Condition = TriggerCondition.Equal,
    TriggerValue = "1",
    Callback = ...
};

// 变化触发：当地址 200 的值发生任何变化时触发（不需要 TriggerValue）
new TriggerConfig()
{
    Address = "200",
    DataType = TriggerDataType.Float,
    Condition = TriggerCondition.Changed,
    // TriggerValue 不需要设置
    Callback = ...
};

// 布尔触发：监视线圈状态
new TriggerConfig()
{
    Address = "500",
    DataType = TriggerDataType.Bool,
    Condition = TriggerCondition.Equal,
    TriggerValue = "true",
    FunctionCode = 0x01,   // 线圈使用功能码 0x01
    Callback = ...
};
```

---

### 回调参数 TriggerEventArgs

```csharp
public class TriggerEventArgs : EventArgs
{
    public ModbusTCPClientPlus Client { get; }  // 客户端实例，可用来做后续读写
    public TriggerConfig Trigger { get; }       // 触发的配置对象
    public object CurrentValue { get; }         // 触发时的当前值
    public object PreviousValue { get; }        // 上一次读取的值
    public DateTime TriggerTime { get; }        // 触发时间
    public bool IsSuccess { get; }              // 读取是否成功
    public string ErrorMessage { get; }         // 失败时的错误信息
    public List<LinkedReadResult> LinkedValues; // 连带读取结果（通过 .Link() 配置）
}
```

**在传统回调方法中使用：**

```csharp
// 传统命名方法回调
private void HandleTrigger(TriggerEventArgs e)
{
    // 1. 检查读取是否成功
    if (!e.IsSuccess)
    {
        Console.WriteLine($"触发器读取失败: {e.ErrorMessage}");
        return;
    }

    // 2. 获取触发信息
    string triggerName = e.Trigger?.Name ?? "未命名";
    object currentVal = e.CurrentValue;
    object previousVal = e.PreviousValue;
    DateTime time = e.TriggerTime;

    Console.WriteLine($"[{time}] 触发器 [{triggerName}] 触发");
    Console.WriteLine($"  当前值: {currentVal}, 上一次值: {previousVal}");

    // 3. 通过 e.Client 做后续操作
    var client = e.Client;
    if (client != null && client.IsConnected)
    {
        // 例如：读取关联数据
        // var result = client.ReadInt32("200");
    }
}
```

---

### 触发后执行操作：`.Then()`

Callback 执行完成后自动调用，在独立线程中执行，不会阻塞触发器轮询。

```csharp
// 流式 API 写法
client.AddTrigger(new TriggerConfig()
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
}));

// 也可以把 Then 的逻辑提取为独立方法
private void ResetAfterAlarm(ModbusTCPClientPlus client)
{
    client.WriteFloat("300", 0.0f);
    client.WriteBool("500", false);
    client.WriteString("600", "OK", 4);
}

// 使用
new TriggerConfig() { ... }
    .Then(ResetAfterAlarm);
```

**`.Then()` 执行顺序：**

```
触发条件满足 → Callback(e) 执行 → 等待 ThenDelayMs → Then(client) 执行
```

```csharp
client.ThenDelayMs = 50;  // Callback 完成后等待 50ms 再执行 Then，默认 0
```

---

### 连带读取：`.Link()`

触发时自动读取其他关联寄存器，结果通过 `e.GetLinkedValue<T>()` 强类型方法获取，无需手动遍历和类型转换。

**Lambda 写法：**

```csharp
client.AddTrigger(new TriggerConfig()
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

        // 也可按地址取值
        float temp = e.GetLinkedValue<float>("400");

        // 安全取值（推荐用于可能失败的场景）
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
    }));
```

**传统命名方法回调中获取连带读取值：**

```csharp
// 定义回调方法
private void OnTriggerWithLinked(TriggerEventArgs e)
{
    // 遍历所有连带读取结果
    if (e.LinkedValues != null)
    {
        foreach (var item in e.LinkedValues)
        {
            if (item.IsSuccess)
            {
                Console.WriteLine($"[{item.Name}] 地址 {item.Address} = {item.Value}");
            }
            else
            {
                Console.WriteLine($"[{item.Name}] 读取失败: {item.ErrorMessage}");
            }
        }
    }

    // 按名称直接取值
    int count = e.GetLinkedValue<int>("产量");
    float pressure = e.GetLinkedValue<float>("压力");

    // 按地址取值
    bool running = e.GetLinkedValue<bool>("500");

    // 使用安全的 TryGet 模式
    if (e.TryGetLinkedValue<float>("压力", out float p) && p > 50)
    {
        Console.WriteLine($"压力过高: {p}");
    }
}

// 配置
var config = new TriggerConfig()
{
    Address = "100",
    DataType = TriggerDataType.Int16,
    Condition = TriggerCondition.Equal,
    TriggerValue = "1",
    Callback = new Action<TriggerEventArgs>(OnTriggerWithLinked)
};
config.LinkedReads.Add(new LinkedReadConfig
{
    Address = "200",
    DataType = TriggerDataType.Int32,
    Name = "产量"
});
config.LinkedReads.Add(new LinkedReadConfig
{
    Address = "300",
    DataType = TriggerDataType.Float,
    Name = "压力"
});
client.AddTrigger(config);
```

---

### Tag 属性用法

`TriggerConfig.Tag` 可存储任意用户自定义数据，方便在回调中识别上下文。

```csharp
// 传入自定义上下文
new TriggerConfig()
{
    Name = "温度监控",
    Address = "300",
    DataType = TriggerDataType.Float,
    Condition = TriggerCondition.GreaterThan,
    TriggerValue = "80",
    Tag = new { Zone = "A区", AlarmLevel = 1, Handler = "张三" },  // 任意对象
    Callback = new Action<TriggerEventArgs>((e) =>
    {
        // 在回调中取出 Tag
        dynamic tag = e.Trigger?.Tag;
        if (tag != null)
        {
            Console.WriteLine($"区域: {tag.Zone}, 级别: {tag.AlarmLevel}");
        }
    })
};
```

---

### 触发器管理 API

```csharp
// 添加触发器（4 个重载）
string id = client.AddTrigger(TriggerConfig config);
string id = client.AddTrigger(TriggerConfig config, Action<TriggerEventArgs> callback);
string id = client.AddTrigger(string name, string address, TriggerDataType dataType,
                              object triggerValue, TriggerCondition condition = Equal,
                              byte functionCode = 0x03);
string id = client.AddTrigger(string name, string address, TriggerDataType dataType,
                              object triggerValue, Action<TriggerEventArgs> callback,
                              TriggerCondition condition = Equal, byte functionCode = 0x03);

// 按 ID 移除
bool removed = client.RemoveTrigger(id);

// 清空所有触发器
client.ClearTriggers();

// 启用/禁用（禁用后不再轮询该触发器）
bool ok = client.SetTriggerEnabled(id, false);

// 重置触发状态（清除 HasTriggered 和 LastValue，允许再次触发）
client.ResetTrigger(id);
client.ResetAllTriggers();

// 查看触发器
List<TriggerConfig> allTriggers = client.GetTriggers();  // 返回副本
int count = client.TriggerCount;                         // 实时数量
```

### 监控生命周期

```csharp
// 启动监控（必须在添加触发器后调用，重复调用无影响）
client.StartTriggerMonitor();

// 停止监控
await client.StopTriggerMonitorAsync();  // 异步：等待当前轮询完成
client.StopTriggerMonitor();             // 同步：阻塞等待

// 查看状态
bool isRunning = client.IsTriggerMonitorRunning;
int pollInterval = client.TriggerPollInterval;  // 轮询间隔，默认 100ms
```

---

### 触发器典型应用场景

**场景 1：设备启动/停止检测**

```csharp
// 当线圈地址 100 变为 true 时，记录设备启动
client.AddTrigger(new TriggerConfig()
{
    Name = "设备启动检测",
    Address = "100",
    DataType = TriggerDataType.Bool,
    Condition = TriggerCondition.Equal,
    TriggerValue = "true",
    FunctionCode = 0x01,
    Callback = new Action<TriggerEventArgs>((e) =>
    {
        Console.WriteLine($"[{e.TriggerTime}] 设备已启动");
    })
});
```

**场景 2：温度超限告警 + 自动复位**

```csharp
client.AddTrigger(new TriggerConfig()
{
    Name = "高温告警",
    Address = "300",
    DataType = TriggerDataType.Float,
    Condition = TriggerCondition.GreaterThan,
    TriggerValue = "85",
    Callback = new Action<TriggerEventArgs>((e) =>
    {
        // 告警逻辑：记录日志、发送通知等
        LogAlarm($"高温告警: {e.CurrentValue}°C");
    })
}.Then(client =>
{
    // 确认告警标志
    client.WriteBool("900", true);
}));
```

**场景 3：值变化监控（数据记录）**

```csharp
client.AddTrigger(new TriggerConfig()
{
    Name = "产量变化记录",
    Address = "200",
    DataType = TriggerDataType.Int32,
    Condition = TriggerCondition.Changed,   // 任何变化都触发
    Callback = new Action<TriggerEventArgs>((e) =>
    {
        // 记录变化：旧值 → 新值
        Console.WriteLine($"产量变化: {e.PreviousValue} → {e.CurrentValue}");
    })
});
```

**场景 4：多条件联动（启停 + 数据采集）**

```csharp
client.AddTrigger(new TriggerConfig()
{
    Name = "生产完成信号",
    Address = "100",
    DataType = TriggerDataType.Int16,
    Condition = TriggerCondition.Equal,
    TriggerValue = "999",  // 完成信号
    Callback = new Action<TriggerEventArgs>((e) =>
    {
        // 触发时同时获取产量和不良品数
        int total = e.GetLinkedValue<int>("总产量");
        int defect = e.GetLinkedValue<int>("不良品");

        double rate = total > 0 ? (double)defect / total * 100 : 0;
        Console.WriteLine($"批次完成! 总产量={total}, 不良品={defect}, 不良率={rate:F2}%");
    })
}
    .Link("200", TriggerDataType.Int32, "总产量")
    .Link("202", TriggerDataType.Int32, "不良品")
    .Then(client =>
    {
        // 复位完成信号
        client.WriteInt16("100", 0);
    }));
```

---

## 心跳检测

心跳通过**读 → 写反转**的方式验证双向通信。每隔 `HeartbeatInterval` 读取心跳地址的当前值，然后写入反转值（0→1, 1→0）。任一步失败即判定连接断开并触发重连。

```
连接成功
  ↓
等待 HeartbeatInterval (默认 1000ms)
  ↓
读取地址 0 → 值=0 → 写入地址 0=1
  ↓
等待 HeartbeatInterval
  ↓
读取地址 0 → 值=1 → 写入地址 0=0
  ↓
... 持续 0↔1 翻转 ...
```

```csharp
client.EnableHeartbeat = true;         // 启用心跳，默认: true
client.HeartbeatAddress = "0";         // 心跳占用地址，默认: "0"
client.HeartbeatFunctionCode = 0x03;   // 功能码，默认: 0x03（保持寄存器）
client.HeartbeatInterval = 1000;       // 间隔 (ms)，默认: 1000
```

> **注意：**
> - 心跳地址会持续 0↔1 翻转，**不可**用于存储业务数据，选用空闲地址。
> - `HeartbeatFunctionCode` 建议使用 `0x03`（保持寄存器）或 `0x01`（线圈），不要使用只读功能码 `0x02`/`0x04`。
> - 心跳每次产生 2 条 `InteractionEvent`（一次读 + 一次写），可在事件中过滤。

---

## 事件

### SuccessfulConnectEvent — 连接成功

```csharp
client.SuccessfulConnectEvent += (DateTime time, string ip, int port) =>
{
    Console.WriteLine($"[{time}] 已连接到 {ip}:{port}");
};
```

### DisconnectionEvent — 连接断开

```csharp
client.DisconnectionEvent += (DateTime time, string ip, int port, Exception ex) =>
{
    Console.WriteLine($"[{time}] 与 {ip}:{port} 断开: {ex?.Message}");
};
```

### InteractionEvent — 每次读写操作

```csharp
client.InteractionEvent += (TimeSpan elapsed, bool success, string method,
                            string address, int count, string value) =>
{
    // 可用于调试、性能监控、操作日志
    string status = success ? "OK" : "FAIL";
    Console.WriteLine($"[{elapsed.TotalMilliseconds:F0}ms] {method} {address}" +
                      $" x{count} = {value} ({status})");
};

// 过滤心跳产生的 InteractionEvent（如果不想看心跳日志）
client.InteractionEvent += (elapsed, success, method, address, count, value) =>
{
    if (address == client.HeartbeatAddress) return; // 跳过心跳地址
    // 处理其他读写...
};
```

### TriggerEvent — 全局触发器事件

所有触发器共用此事件（不同于单个 `TriggerConfig.Callback`，后者只在该触发器触发时执行）。

```csharp
// Lambda 方式
client.TriggerEvent += (sender, e) =>
{
    Console.WriteLine($"[全局] 触发器触发: {e.Trigger?.Name} = {e.CurrentValue}");
};

// 传统 EventHandler 方式
private void OnAnyTrigger(object sender, TriggerEventArgs e)
{
    var client = (ModbusTCPClientPlus)sender;
    Console.WriteLine($"触发器 [{e.Trigger?.Name}] 触发");
    Console.WriteLine($"  地址: {e.Trigger?.Address}");
    Console.WriteLine($"  当前值: {e.CurrentValue}");
    Console.WriteLine($"  触发时间: {e.TriggerTime}");
}

client.TriggerEvent += OnAnyTrigger;
```

---

## 配置参考

### ModbusTCPClientPlus 属性

| 属性 | 类型 | 默认值 | 读写 | 说明 |
|---|---|---|---|---|
| `TargetIP` | `string` | -- | 只读 | 服务器 IP |
| `TargetPort` | `int` | -- | 只读 | 端口号 |
| `IsConnected` | `bool` | -- | 只读 | 是否已连接 |
| `StationNumber` | `ushort` | `1` | 读写 | Modbus 站号 |
| `AddressStartsAtOne` | `bool` | `false` | 读写 | `true`=地址从 1 开始，自动减 1 |
| `Order` | `ByteOrder` | `CDAB` | 读写 | 字节序 |
| `ReverseString` | `bool` | `false` | 读写 | `true`=读写字符串时颠倒字节序 |
| `ReadTimeout` | `int` | `5000` | 读写 | 读写超时 (ms) |
| `ConnectionTimeout` | `int` | `5000` | 读写 | 连接超时 (ms) |
| `AutoReconnect` | `bool` | `true` | 读写 | 是否自动重连 |
| `ReconnectInterval` | `int` | `3000` | 读写 | 重连间隔 (ms) |
| `MaxReconnectAttempts` | `int` | `0` | 读写 | 最大重连次数，`0`=无限 |
| `IsReconnecting` | `bool` | -- | 只读 | 是否正在重连中 |
| `EnableHeartbeat` | `bool` | `true` | 读写 | 启用心跳检测 |
| `HeartbeatAddress` | `string` | `"0"` | 读写 | 心跳地址 |
| `HeartbeatFunctionCode` | `byte` | `0x03` | 读写 | 心跳功能码 |
| `HeartbeatInterval` | `int` | `1000` | 读写 | 心跳间隔 (ms) |
| `TriggerPollInterval` | `int` | `100` | 读写 | 触发器轮询间隔 (ms) |
| `ThenDelayMs` | `int` | `0` | 读写 | Callback 完成后到 Then 的延迟 (ms) |
| `IsTriggerMonitorRunning` | `bool` | -- | 只读 | 触发监控是否运行中 |
| `TriggerCount` | `int` | -- | 只读 | 当前触发器数量 |

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

**转换示例**（32 位整数 `0x12345678`）：

| 字节序 | 内存中的字节序列 |
|---|---|
| ABCD | `12 34 56 78` |
| BADC | `34 12 78 56` |
| CDAB | `56 78 12 34` |
| DCBA | `78 56 34 12` |

---

## 枚举和静态类

### TriggerDataType

```csharp
Bit, Bool, Int16, UInt16, Int32, UInt32, Int64, Float, Double, String
```

### TriggerCondition

```csharp
Equal, NotEqual, GreaterThan, LessThan,
GreaterThanOrEqual, LessThanOrEqual, Changed
```

### FunctionCode（静态类）

```csharp
ModbusTCPClientPlus.FunctionCode.ReadCoils              // 0x01 读线圈
ModbusTCPClientPlus.FunctionCode.ReadDiscreteInputs     // 0x02 读离散输入
ModbusTCPClientPlus.FunctionCode.ReadHoldingRegisters   // 0x03 读保持寄存器
ModbusTCPClientPlus.FunctionCode.ReadInputRegisters     // 0x04 读输入寄存器
ModbusTCPClientPlus.FunctionCode.WriteSingleCoil         // 0x05 写单个线圈
ModbusTCPClientPlus.FunctionCode.WriteSingleRegister     // 0x06 写单个寄存器
ModbusTCPClientPlus.FunctionCode.WriteMultipleCoils      // 0x0F 写多个线圈
ModbusTCPClientPlus.FunctionCode.WriteMultipleRegisters  // 0x10 写多个寄存器
```

### ExceptionCode（静态类）

```csharp
// 常量
ExceptionCode.IllegalFunction                     // 0x01 非法功能码
ExceptionCode.IllegalDataAddress                  // 0x02 非法数据地址
ExceptionCode.IllegalDataValue                    // 0x03 非法数据值
ExceptionCode.ServerDeviceFailure                 // 0x04 服务器设备故障
ExceptionCode.Acknowledge                         // 0x05 确认
ExceptionCode.ServerDeviceBusy                     // 0x06 服务器设备忙
ExceptionCode.MemoryParityError                   // 0x08 存储器奇偶校验错误
ExceptionCode.GatewayPathUnavailable              // 0x0A 网关路径不可用
ExceptionCode.GatewayTargetDeviceFailedToRespond  // 0x0B 网关目标设备无响应

// 方法
string desc = ModbusTCPClientPlus.ExceptionCode.GetDescription(0x02);
// 返回: "非法数据地址"
```

---

## 释放资源

用完后必须调用 `Dispose()` 释放网络资源和后台任务。

```csharp
// 方式 1：手动释放
client.Dispose();

// 方式 2：using 语句（推荐）
using (var client = new ModbusTCPClientPlus("192.168.1.100", 502))
{
    var r = await client.ReadFloatAsync("100");
    // ... 其他操作
} // 自动调用 Dispose()
```

`Dispose()` 按顺序执行：停止触发器监控 → 停止心跳检测 → 取消重连 → 取消待处理操作 → 关闭 TCP 连接 → 释放信号量。

---

## 完整示例

### WinForms 应用（传统写法，不依赖 Lambda）

```csharp
using ModbusIntegration.Modbus;
using static ModbusIntegration.Modbus.ModbusTCPClientPlus;

public partial class Form1 : Form
{
    ModbusTCPClientPlus client;

    public Form1()
    {
        InitializeComponent();

        // 创建客户端
        client = new ModbusTCPClientPlus("192.168.1.100", 502);
        client.ThenDelayMs = 30;
        client.TriggerPollInterval = 200;

        // 订阅事件（使用命名方法而非 Lambda）
        client.SuccessfulConnectEvent += OnConnected;
        client.DisconnectionEvent += OnDisconnected;
        client.InteractionEvent += OnInteraction;
        client.TriggerEvent += OnGlobalTrigger;

        // 配置触发器
        SetupTriggers();

        // 启动触发器监控
        client.StartTriggerMonitor();
    }

    // === 事件处理方法 ===

    private void OnConnected(DateTime time, string ip, int port)
    {
        Log($"已连接 {ip}:{port}");
    }

    private void OnDisconnected(DateTime time, string ip, int port, Exception ex)
    {
        Log($"断开: {ex?.Message}");
    }

    private void OnInteraction(TimeSpan elapsed, bool success, string method,
                               string address, int count, string value)
    {
        // 过滤心跳日志
        if (address == client.HeartbeatAddress) return;

        string status = success ? "OK" : "FAIL";
        Log($"[{elapsed.TotalMilliseconds:F0}ms] {method} {address} x{count} = {value} ({status})");
    }

    private void OnGlobalTrigger(object sender, TriggerEventArgs e)
    {
        Log($"[全局事件] 触发器 [{e.Trigger?.Name}] = {e.CurrentValue}");
    }

    // === 触发器配置 ===

    private void SetupTriggers()
    {
        // 触发器 1：高温告警 + 连带读取 + 自动复位
        var highTempTrigger = new TriggerConfig()
        {
            Name = "高温告警",
            Address = "300",
            DataType = TriggerDataType.Float,
            Condition = TriggerCondition.GreaterThan,
            TriggerValue = "80",
            Callback = new Action<TriggerEventArgs>(OnHighTemperature)
        };
        // 使用流式 API 添加 Link 和 Then
        highTempTrigger
            .Link("200", TriggerDataType.Int32, "产量")
            .Link("400", TriggerDataType.Float, "压力")
            .Then(ResetAfterAlarm);
        client.AddTrigger(highTempTrigger);

        // 触发器 2：设备启动检测（简化重载）
        client.AddTrigger(
            name: "设备启动",
            address: "100",
            dataType: TriggerDataType.Bool,
            triggerValue: "true",
            callback: new Action<TriggerEventArgs>(OnDeviceStart),
            condition: TriggerCondition.Equal,
            functionCode: 0x01
        );

        // 触发器 3：产量变化记录
        client.AddTrigger(
            name: "产量变化",
            address: "200",
            dataType: TriggerDataType.Int32,
            triggerValue: null,  // Changed 条件不需要 TriggerValue
            callback: new Action<TriggerEventArgs>(OnProductionChanged),
            condition: TriggerCondition.Changed
        );
    }

    // === 触发器回调方法 ===

    private void OnHighTemperature(TriggerEventArgs e)
    {
        // 使用强类型方法获取连带读取值
        int count = e.GetLinkedValue<int>("产量");
        float pressure = e.GetLinkedValue<float>("压力");

        Log($"⚠ 高温告警! 温度={e.CurrentValue}°C, 产量={count}, 压力={pressure}");

        // 也可以使用 TryGet 模式
        if (e.TryGetLinkedValue<float>("压力", out float p) && p > 50)
        {
            Log($"压力同时过高: {p}");
        }
    }

    private void OnDeviceStart(TriggerEventArgs e)
    {
        Log($"[{e.TriggerTime}] 设备已启动");

        // 通过 e.Client 执行后续操作
        var client = e.Client;
        if (client != null && client.IsConnected)
        {
            // 例如读取设备运行参数
            var freq = client.ReadFloat("310");
            if (freq.IsSuccess)
                Log($"当前频率: {freq.Value} Hz");
        }
    }

    private void OnProductionChanged(TriggerEventArgs e)
    {
        Log($"产量变化: {e.PreviousValue} → {e.CurrentValue}");
    }

    // === Then 回调方法 ===

    private void ResetAfterAlarm(ModbusTCPClientPlus client)
    {
        client.WriteFloat("300", 0.0f);     // 温度设定值归零
        client.WriteBool("500", false);     // 告警标志复位
    }

    // === UI 操作 ===

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
        if (float.TryParse(txtValue.Text, out float val))
        {
            var w = await client.WriteFloatAsync("300", val);
            Log(w.IsSuccess ? $"写入成功: {val}" : $"写入失败: {w.ErrorMessage}");
        }
    }

    // === 日志辅助 ===

    private void Log(string msg)
    {
        if (InvokeRequired)
            BeginInvoke(new Action(() => richTextBox.AppendText($"{DateTime.Now:HH:mm:ss} {msg}\n")));
        else
            richTextBox.AppendText($"{DateTime.Now:HH:mm:ss} {msg}\n");
    }

    // === 清理 ===

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        client?.Dispose();
        base.OnFormClosed(e);
    }
}
```

### 控制台应用

```csharp
using ModbusIntegration.Modbus;

class Program
{
    static async Task Main(string[] args)
    {
        using var client = new ModbusTCPClientPlus("192.168.1.100", 502);

        // 等待连接
        client.SuccessfulConnectEvent += (t, ip, port) =>
            Console.WriteLine($"已连接 {ip}:{port}");

        await Task.Delay(1000); // 给连接一点时间

        if (!client.IsConnected)
        {
            Console.WriteLine("连接失败");
            return;
        }

        // 循环读取
        while (!Console.KeyAvailable)
        {
            var temp = await client.ReadFloatAsync("300");
            var press = await client.ReadFloatAsync("400");
            var running = await client.ReadBoolAsync("500");

            Console.Write($"\r温度={temp.Value:F1}°C  压力={press.Value:F2}  运行={running.Value}");
            await Task.Delay(1000);
        }
    }
}
```

---

## 常见问题

**Q: 读写返回 "未连接到服务器"？**

A: 构造函数的连接是异步的，构造完成时连接可能尚未建立。监听 `SuccessfulConnectEvent` 确认连接成功后再进行读写。

**Q: 读取的值与实际不符？**

A: 最常见的原因是字节序不对。尝试逐一换成 `ABCD`、`DCBA`、`BADC`、`CDAB` 测试。

**Q: 地址从 0 还是 1 开始？**

A: 默认从 0 开始。设置 `client.AddressStartsAtOne = true` 改为从 1 开始，库会自动将传入地址减 1。

**Q: 触发器添加后不触发？**

A: 检查：
1. 是否调用了 `StartTriggerMonitor()`
2. `IsEnabled` 是否为 `true`
3. `Condition` 和 `TriggerValue` 配置是否正确
4. 服务器是否可达（`IsConnected` 为 `true`）

**Q: 触发器会重复触发吗？**

A: 不会。触发后 `HasTriggered` 置为 `true`，直到条件不再满足时才重置。如需重新触发，调用 `ResetTrigger(id)` 即可。

**Q: 心跳地址值被修改了？**

A: 心跳会持续翻转心跳地址的值（0↔1），这是正常行为。确保心跳地址不与业务数据地址冲突。

**Q: 回调中可以用 async/await 吗？**

A: 可以。`Callback` 的类型是 `Action<TriggerEventArgs>`，但 Lambda 中可以写 `async`，回调内部可以 `await`。

**Q: `.Then()` 中写操作失败怎么办？**

A: 异常会被捕获并输出到 `Debug.WriteLine`，不会影响触发器监控。建议在 Then 委托内部自己 try-catch 关键操作。
