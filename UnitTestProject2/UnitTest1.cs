using HslCommunication.ModBus;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace UnitTestProject2
{
    [TestClass]
    public class UnitTest1
    {
        private ModbusTcpNet modbus;
        [TestMethod]
        public async void TestMethod1()
        {
            modbus = new ModbusTcpNet("127.0.0.1", 502);
            modbus.DataFormat = HslCommunication.Core.DataFormat.CDAB;
            var isconnect = await modbus.ConnectServerAsync();
            do
            {

            } while (!isconnect.IsSuccess);
            var tasks = new List<Task>();
            for (int i = 0; i < 10; i++)
            {
                tasks.Add(Task.Run(() =>
                {
                    for (int j = 0; j < 1000; j++)
                    {
                        var t1 = modbus.Write("100", (short)100);
                        Assert.AreEqual(true, t1.IsSuccess);
                        var t2 = modbus.Write("200", (int)100);
                        Assert.AreEqual(true, t2.IsSuccess);
                        var t3 = modbus.Write("300", (float)0.5655f);
                        Assert.AreEqual(true, t3.IsSuccess);
                        var t4 = modbus.Write("400", (double)0.5655d);
                        Assert.AreEqual(true, t4.IsSuccess);
                    }

                }));
            }

        }
    }
}
