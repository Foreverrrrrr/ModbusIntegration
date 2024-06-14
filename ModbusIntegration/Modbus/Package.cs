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
        /// 异常码
        /// </summary>
        public byte ExceptionCode { get; set; }
        /// <summary>
        /// 错误信息
        /// </summary>
        public string ErrorMessage { get; set; }
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
        public byte[][] SendBuff { get; set; }
        /// <summary>
        /// 返回报文
        /// </summary>
        public byte[][] ReceiveBuff { get; set; }
        /// <summary>
        /// 数据数组
        /// </summary>
        public byte[] DataBuff { get; set; }

        /// <summary>
        /// 获取发送报文的十六进制字符串
        /// </summary>
        public string GetSendHex()
        {
            if (SendBuff == null) return string.Empty;
            return string.Join(" | ", SendBuff.Select(b => BitConverter.ToString(b).Replace("-", " ")));
        }

        /// <summary>
        /// 获取接收报文的十六进制字符串
        /// </summary>
        public string GetReceiveHex()
        {
            if (ReceiveBuff == null) return string.Empty;
            return string.Join(" | ", ReceiveBuff.Select(b => BitConverter.ToString(b).Replace("-", " ")));
        }

        /// <summary>
        /// 获取简要结果描述
        /// </summary>
        public override string ToString()
        {
            return IsSuccess
                ? $"[成功] 地址:{Address} 值:{Value} 耗时:{ElapsedTime.TotalMilliseconds:F2}ms"
                : $"[失败] 地址:{Address} 错误:{ErrorMessage ?? "未知错误"} 耗时:{ElapsedTime.TotalMilliseconds:F2}ms";
        }
    }
}
