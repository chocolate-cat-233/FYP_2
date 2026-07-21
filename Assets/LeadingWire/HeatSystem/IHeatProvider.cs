using System;

namespace HeatSystem
{
    /// <summary>
    /// 热量提供者接口。热源(HeatSource)和节点(HeatNode)都实现此接口。
    /// </summary>
    public interface IHeatProvider
    {
        /// <summary>当前是否正在输出热量</summary>
        bool IsOutputting { get; }

        /// <summary>输出状态变化时触发</summary>
        event Action OnOutputChanged;
    }
}
