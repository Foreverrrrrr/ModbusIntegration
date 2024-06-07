using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ModbusIntegration.Modbus
{
    public class Package<T>
    {
        /// <summary>
        /// 耗时
        /// </summary>
        public TimeSpan ElapsedTime { get; set; }
        /// <summary>
        /// 操作结果
        /// </summary>
        public bool IsSuccess { get; set; }
        /// <summary>
        /// 功能码
        /// </summary>
        public byte FunctionCode { get; set; }
        /// <summary>
        /// 起始地址
        /// </summary>
        public string Address { get; set; }
        /// <summary>
        /// ModbusTCP数据值
        /// </summary>
        public T Value { get; set; }
        /// <summary>
        /// 发送报文
        /// </summary>
        public byte[] SendBuff { get; set; }
        /// <summary>
        /// 返回报文
        /// </summary>
        public byte[] ReceiveBuff { get; set; }
        /// <summary>
        /// 数据数组
        /// </summary>
        public byte[] DataBuff { get; set; }
    }
}
