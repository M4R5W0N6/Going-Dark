using UnityEngine;
using UnityEngine.EventSystems;

public class NetworkUpdateEvent : BaseEventData
{
    public static readonly ExecuteEvents.EventFunction<IEventListener> Delegate = delegate (IEventListener handler, BaseEventData data)
    {
        var casted = ExecuteEvents.ValidateEventData<NetworkUpdateEvent>(data);
        handler.NetworkUpdateCallback(casted);
    };

    public string state;

    public NetworkUpdateEvent(EventSystem eventSystem, string state) : base(eventSystem)
    {
        this.state = state;
    }
}
