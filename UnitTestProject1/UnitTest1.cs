using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModbusIntegration.Modbus;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UnitTestProject1
{
    [TestClass]
    public class UnitTest1
    {
        ModbusTCPClientPlus modbusTCP_Client;
        [TestMethod]
        public void TestMethod1()
        {
            var modbusTCP_Client = new ModbusTCPClientPlus("127.0.0.1", 5111);
            modbusTCP_Client.SuccessfulConnectEvent += (t, ip, port) =>
            {
                Console.WriteLine("连接到服务器");
            };
            modbusTCP_Client.DisconnectionEvent += (t, ip, port, ex) =>
            {
                Console.WriteLine($"断开服务器: {ex?.Message}");
            };
            modbusTCP_Client.InteractionEvent += (t, t1, t2, t3, t4, t5) =>
            {
                Console.WriteLine($"耗时：{t.TotalMilliseconds}ms, 操作结果：{t1}，方法：{t2}，地址：{t3}，寄存器个数：{t4}，Value：{t5}");
            };

            // 等待连接
            do
            {
                Thread.Sleep(100);
            } while (!modbusTCP_Client.IsConnected);

            var tasks = new List<Task>();
            for (int i = 0; i < 5; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    for (int j = 0; j < 500; j++)
                    {
                        // 1. ReadInt32 (单值读取)
                        var r1 = await modbusTCP_Client.ReadInt32Async("10");
                        Assert.AreEqual(true, r1.IsSuccess);

                        // 2. ReadBool (单个布尔值读取)
                        var r2 = await modbusTCP_Client.ReadBoolAsync("30");
                        Assert.AreEqual(true, r2.IsSuccess);

                        // 3. ReadDoubleArray (多个双精度浮点数读取)
                        var r3 = await modbusTCP_Client.ReadDoubleArrayAsync("100", 100);
                        Assert.AreEqual(true, r3.IsSuccess);

                        // 4. ReadFloatArray (多个单精度浮点数读取)
                        var r4 = await modbusTCP_Client.ReadFloatArrayAsync("200", 200);
                        Assert.AreEqual(true, r4.IsSuccess);

                        // 5. ReadInt32Array (多个32位整数读取)
                        var r5 = await modbusTCP_Client.ReadInt32ArrayAsync("10", 100);
                        Assert.AreEqual(true, r5.IsSuccess);

                        // 6. ReadBoolArray (多个布尔值读取)
                        var r6 = await modbusTCP_Client.ReadBoolArrayAsync("30", 200);
                        Assert.AreEqual(true, r6.IsSuccess);

                        // 7. ReadUInt16Array (字节数组读取 - 用UInt16Array代替)
                        var r7 = await modbusTCP_Client.ReadUInt16ArrayAsync("1000", 250);
                        Assert.AreEqual(true, r7.IsSuccess);

                        // 8. ReadInt16 (16位整数读取)
                        var r8 = await modbusTCP_Client.ReadInt16Async("100");
                        Assert.AreEqual(true, r8.IsSuccess);

                        // 9. ReadInt16Array (多个16位整数读取)
                        var r9 = await modbusTCP_Client.ReadInt16ArrayAsync("100", 100);
                        Assert.AreEqual(true, r9.IsSuccess);

                        // 10. ReadFloatArray (多个浮点数读取)
                        var r10 = await modbusTCP_Client.ReadFloatArrayAsync("200", 100);
                        Assert.AreEqual(true, r10.IsSuccess);

                        // 11. ReadDoubleArray (多个双精度浮点数读取)
                        var r11 = await modbusTCP_Client.ReadDoubleArrayAsync("300", 100);
                        Assert.AreEqual(true, r11.IsSuccess);

                        // 12. ReadString (字符串读取)
                        var r12 = await modbusTCP_Client.ReadStringAsync("400", 10);
                        Assert.AreEqual(true, r12.IsSuccess);

                        // === 写入测试 ===

                        // 1. WriteBool (单个布尔值写入)
                        var w1 = await modbusTCP_Client.WriteBoolAsync("10", true);
                        Assert.AreEqual(true, w1.IsSuccess);

                        // 2. WriteBoolArray (多个布尔值写入)
                        var w2 = await modbusTCP_Client.WriteBoolArrayAsync("30", new bool[] { true, false, true });
                        Assert.AreEqual(true, w2.IsSuccess);

                        // 3. WriteInt16 (16位整数写入)
                        var w3 = await modbusTCP_Client.WriteInt16Async("100", 12345);
                        Assert.AreEqual(true, w3.IsSuccess);

                        // 4. WriteInt32 (32位整数写入)
                        var w4 = await modbusTCP_Client.WriteInt32Async("200", 98765);
                        Assert.AreEqual(true, w4.IsSuccess);

                        // 5. WriteFloat (单精度浮点数写入)
                        var w5 = await modbusTCP_Client.WriteFloatAsync("300", 123.45f);
                        Assert.AreEqual(true, w5.IsSuccess);

                        // 6. WriteDouble (双精度浮点数写入)
                        var w6 = await modbusTCP_Client.WriteDoubleAsync("400", 6789.123);
                        Assert.AreEqual(true, w6.IsSuccess);

                        // 7. WriteString (字符串写入)
                        var w7 = await modbusTCP_Client.WriteStringAsync("500", "Test String", 20);
                        Assert.AreEqual(true, w7.IsSuccess);
                    }
                }));
            }
            Task.WaitAll(tasks.ToArray());

            modbusTCP_Client.Dispose();
        }
    }
}