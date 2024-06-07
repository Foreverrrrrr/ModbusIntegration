using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ModbusIntegration.Modbus;

namespace ModbusIntegration
{
    public partial class Form1 : Form
    {
        AsyncSharpTcpClient modbusTCP_Client;
        public Form1()
        {
            InitializeComponent();
            modbusTCP_Client = new AsyncSharpTcpClient("127.0.0.1", 502);
            modbusTCP_Client.SuccessfuConnectEvent += (t, ip) =>
            {
                RetextADD("连接到服务器");
            };
            modbusTCP_Client.DisconnectionEvent += (t, ip) =>
            {
                RetextADD("断开到服务器");
            };
            modbusTCP_Client.InteractionEvent += (t, t1, t2, t3, t4, t5) =>
            {
                RetextADD($"耗时：{t.TotalMilliseconds},操作结果：{t1},方法：{t2}，地址{t3}，寄存器个数{t4}，Value：{t5}");
            };
        }

        private void RetextADD(string str)
        {
            this.Invoke(new Action(() =>
            {
                if (richTextBox1.Text.Length > 1000)
                {
                    richTextBox1.Text = "";
                }
                richTextBox1.Text = richTextBox1.Text.Insert(0, DateTime.Now.ToString() + "  " + str + "\r\n");
            }));
        }

        private void button1_Click(object sender, System.EventArgs e)
        {

        }

        private void button2_Click(object sender, System.EventArgs e)
        {
            //var t1 = modbusTCP_Client.ReadIn32("100", 30);
            //RetextADD($"读取{t1.IsSuccess},{t1.Value}");

            var t1 = modbusTCP_Client.ReadIn32("10");
        }

        private void button3_Click(object sender, EventArgs e)
        {
            var t1 = modbusTCP_Client.ReadIn16("200");
            // var t1 = modbusTCP_Client.ReadIn16("200", 10);
        }

        private void button1_Click_1(object sender, EventArgs e)
        {
            var t1 = modbusTCP_Client.ReadInFloat("300");
        }

        private void button4_Click(object sender, EventArgs e)
        {
            var t1 = modbusTCP_Client.ReadInDouble("400", 10);

        }

        private void button5_Click(object sender, EventArgs e)
        {
            var t1 = modbusTCP_Client.ReadInString("500", 10);
        }

        private void button6_Click(object sender, EventArgs e)
        {
            var t1 = modbusTCP_Client.ReadBool("600", 10);
        }

        private void button7_Click(object sender, EventArgs e)
        {
            modbusTCP_Client.WriteBool("100", new bool[] { true, true });
        }

        private void button8_Click(object sender, EventArgs e)
        {
            modbusTCP_Client.WriteIn16("0", 15);
        }

        private void button9_Click(object sender, EventArgs e)
        {
            modbusTCP_Client.WriteIn32("10", 100);
        }
    }
}
