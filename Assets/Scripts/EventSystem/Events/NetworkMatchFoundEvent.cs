using UnityEngine;
using UnityEngine.EventSystems;

public class NetworkMatchFoundEvent : BaseEventData
{
    public static readonly ExecuteEvents.EventFunction<IEventListener> Delegate = delegate (IEventListener handler, BaseEventData data)
    {
        var casted = ExecuteEvents.ValidateEventData<NetworkMatchFoundEvent>(data);
        handler.NetworkMatchFoundCallback(casted);
    };

    public NetworkMatchFoundEvent(EventSystem eventSystem) : base(eventSystem)
    {

    }
}
