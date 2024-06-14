using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModbusIntegration.Modbus;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModbusIntegration.Modbus;

namespace UnitTestProject1
{
    [TestClass]
    public class UnitTest1
    {
        AsyncSharpTcpClient modbusTCP_Client;
        [TestMethod]
        public void TestMethod1()
        {
            modbusTCP_Client = new AsyncSharpTcpClient("127.0.0.1", 502);
            modbusTCP_Client.SuccessfuConnectEvent += (t, ip) =>
            {
                Console.WriteLine("连接到服务器");

            };
            modbusTCP_Client.DisconnectionEvent += (t, ip) =>
            {
                Console.WriteLine("断开到服务器");
            };
            modbusTCP_Client.InteractionEvent += (t, t1, t2, t3, t4, t5) =>
            {
                Console.WriteLine($"耗时：{t.TotalMilliseconds},操作结果：{t1},方法：{t2}，地址{t3}，寄存器个数{t4}，Value：{t5}");
            };
            do
            {

            } while (!modbusTCP_Client.IsConnect);

            var tasks = new List<Task>();

            tasks.Add(Task.Run(() =>
            {
                for (int i = 0; i < 10000; i++)
                {
                    var r1 = modbusTCP_Client.ReadIn32("10");
                    Assert.AreEqual(r1.IsSuccess, true);
                    var r2 = modbusTCP_Client.ReadBool("30");
                    Assert.AreEqual(r2.IsSuccess, true);
                    var r3 = modbusTCP_Client.WriteFloat("10", 0.2311f);
                    Assert.AreEqual(r3.IsSuccess, true);
                    var r4 = modbusTCP_Client.WriteString("10", "r4");
                    Assert.AreEqual(r4.IsSuccess, true);
                }
            }));
            Task.WaitAll(tasks.ToArray()); // 等待所有任务完成
        }
    }
}
