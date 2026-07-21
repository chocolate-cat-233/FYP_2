namespace HeatSystem
{
    /// <summary>
    /// 热量接收者接口。节点(HeatNode)实现此接口，接收来自热管的热量输入。
    /// </summary>
    public interface IHeatAcceptor
    {
        /// <summary>
        /// 接收来自指定热管的热量信号。
        /// </summary>
        /// <param name="line">发送信号的热管</param>
        /// <param name="hasHeat">该热管是否正在输送热量</param>
        void ReceiveHeat(HeatLine line, bool hasHeat);
    }
}
