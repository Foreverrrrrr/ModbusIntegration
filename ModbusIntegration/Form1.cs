using ModbusIntegration.Modbus;
using System;
using System.Windows.Forms;
using static ModbusIntegration.Modbus.ModbusTCPClientPlus;

namespace ModbusIntegration
{
    public partial class Form1 : Form
    {
        ModbusTCPClientPlus modbusTCP_Client;
        public Form1()
        {
            InitializeComponent();
            //modbusTCP_Client = new AsyncSharpTcpClient("122.51.121.66", 502);
            modbusTCP_Client = new ModbusTCPClientPlus("127.0.0.1", 502);
            modbusTCP_Client.SuccessfulConnectEvent += (t, ip, port) =>
            {
                RetextADD("连接到服务器");
            };
            modbusTCP_Client.DisconnectionEvent += (t, ip, port, ex) =>
            {
                RetextADD("断开到服务器");
            };
            modbusTCP_Client.InteractionEvent += (t, t1, t2, t3, t4, t5) =>
            {
                RetextADD($"耗时：{t.TotalMilliseconds},操作结果：{t1},方法：{t2}，地址{t3}，寄存器个数{t4}，Value：{t5}");
            };
            // 示例：触发时连带读取其他寄存器，结果通过 e.LinkedValues 获取
            modbusTCP_Client.AddTrigger(new TriggerConfig()
            {
                Address = "300",
                DataType = TriggerDataType.Int16,
                Condition = TriggerCondition.Equal,
                TriggerValue = "80",
                Callback = new Action<TriggerEventArgs>((e) =>
                {
                    var client = e.Client;
                    // 按名称或地址直接拿强类型值，无需手动遍历和转换
                    int count = e.GetLinkedValue<int>("产量计数");
                    float pressure = e.GetLinkedValue<float>("400");
                    bool running = e.GetLinkedValue<bool>("运行状态");

                    RetextADD($"触发值={e.CurrentValue}, 产量={count}, 压力={pressure}, 运行={running}");
                })
            }
            .Link("200", TriggerDataType.Int32, "产量计数")
            .Link("400", TriggerDataType.Float, "压力值")
            .Link("500", TriggerDataType.Bool, "运行状态")
            .Then(client =>
                {
                    client.WriteFloat("300", 0.0f);
                }));

            modbusTCP_Client.StartTriggerMonitor();
        }

        private void RetextADD(string str)
        {
            this.Invoke(new Action(() =>
            {
                if (richTextBox1.Text.Length > 1000)
                {
                    richTextBox1.Text = string.Empty;
                }
                richTextBox1.Text = richTextBox1.Text.Insert(0, DateTime.Now.ToString() + "  " + str + "\r\n");
            }));
        }

        private void button1_Click(object sender, System.EventArgs e)
        {

        }

        private async void button2_Click(object sender, System.EventArgs e)
        {
            var t1 = modbusTCP_Client.ReadInt32Array("200", 10);
            var t2 = await modbusTCP_Client.ReadInt32ArrayAsync("200", 10);

        }

        private async void button3_Click(object sender, EventArgs e)
        {
            var t1 = modbusTCP_Client.ReadInt16Array("300", 100);
            var t2 = await modbusTCP_Client.ReadInt16ArrayAsync("300", 100);
            // var t1 = modbusTCP_Client.ReadIn16("200", 10);
        }

        private async void button1_Click_1(object sender, EventArgs e)
        {
            var t1 = modbusTCP_Client.ReadFloatArray("400", 200);
            var t2 = await modbusTCP_Client.ReadFloatArrayAsync("400", 200);
        }

        private async void button4_Click(object sender, EventArgs e)
        {
            var t1 = modbusTCP_Client.ReadDouble("500");
            var t2 = await modbusTCP_Client.ReadDoubleAsync("500");
        }

        private async void button5_Click(object sender, EventArgs e)
        {
            var t1 = modbusTCP_Client.ReadString("600", 20);
            var t2 = await modbusTCP_Client.ReadStringAsync("600", 20);
        }

        private async void button6_Click(object sender, EventArgs e)
        {
            var t1 = modbusTCP_Client.ReadBit("800.2");
            var t2 = modbusTCP_Client.ReadBitArray("800", 2);
            var t3 = await modbusTCP_Client.ReadBoolArrayAsync("800", 2);
        }

        private void button7_Click(object sender, EventArgs e)
        {
            //modbusTCP_Client.WriteBoolArray("110", new bool[] { true, false, true, true });
            //modbusTCP_Client.WriteBool("200", true);
            var t1 = modbusTCP_Client.WriteBitArray("800", new bool[]
            {
              true, false, true, true,
              true, false, true, true,
              true, false, true, true,
              true, false, true, false,true
            });
            //modbusTCP_Client.WriteBit("800", 1, true);
        }

        private void button8_Click(object sender, EventArgs e)
        {
            var w3 = modbusTCP_Client.WriteInt16("300", 12345);
        }

        private void button9_Click(object sender, EventArgs e)
        {
            modbusTCP_Client.WriteInt32("200", 98765);
        }

        private void button10_Click(object sender, EventArgs e)
        {
            modbusTCP_Client.WriteFloat("400", 2.453f);
        }

        private void button11_Click(object sender, EventArgs e)
        {
            modbusTCP_Client.WriteDouble("500", 55.525d);
        }

        private void button12_Click(object sender, EventArgs e)
        {
            modbusTCP_Client.WriteString("600", "sdsad1", 6);
        }

        private async void button13_Click(object sender, EventArgs e)
        {
            // 1. WriteBool (单个布尔值写入)
            var w1 = modbusTCP_Client.WriteBool("10", true);

            // 2. WriteBool (多个布尔值写入)
            var w2 = modbusTCP_Client.WriteBoolArray("30", new bool[] { true, false, true });

            // 3. WriteIn16 (16位整数写入)
            var w3 = await modbusTCP_Client.WriteInt16Async("100", (short)12345);

            // 4. WriteIn32 (32位整数写入)
            var w4 = modbusTCP_Client.WriteInt32("200", 98765);

            // 5. WriteFloat (单精度浮点数写入)
            var w5 = modbusTCP_Client.WriteFloat("300", 123.45f);

            // 6. WriteDouble (双精度浮点数写入)
            var w6 = modbusTCP_Client.WriteDouble("400", 6789.123);

            // 7. WriteString (字符串写入)
            var w7 = modbusTCP_Client.WriteString("500", "Test String", 8);
        }
    }
}
