using UnityEngine;
using UnityEngine.EventSystems;

public class ClientConnectedEvent : BaseEventData
{
    public static readonly ExecuteEvents.EventFunction<IEventListener> Delegate = delegate (IEventListener handler, BaseEventData data)
    {
        var casted = ExecuteEvents.ValidateEventData<ClientConnectedEvent>(data);
        handler.ClientConnectedCallback(casted);
    };

    public ulong obj;

    public ClientConnectedEvent(EventSystem eventSystem, ulong obj) : base(eventSystem)
    {
        this.obj = obj;
    }
}
