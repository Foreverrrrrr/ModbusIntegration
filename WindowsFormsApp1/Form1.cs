using HslCommunication.ModBus;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WindowsFormsApp1
{
    public partial class Form1 : Form
    {
        private ModbusTcpNet modbus;

        public Form1()
        {
            InitializeComponent();
            intooAsync();
        }

        public void intooAsync()
        {
            modbus = new ModbusTcpNet("127.0.0.1", 502);
            modbus.DataFormat = HslCommunication.Core.DataFormat.CDAB;
            var isconnect = modbus.ConnectServer();
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
                        var t2 = modbus.Write("200", (int)100);
                        var t3 = modbus.Write("300", (float)0.5655f);
                        var t4 = modbus.Write("400", (double)0.5655d);
                    }
                }));
            }
        }

    }
}
