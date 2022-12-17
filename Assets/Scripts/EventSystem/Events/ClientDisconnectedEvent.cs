using UnityEngine;
using UnityEngine.EventSystems;

public class ClientDisconnectedEvent : BaseEventData
{
    public static readonly ExecuteEvents.EventFunction<IEventListener> Delegate = delegate (IEventListener handler, BaseEventData data)
    {
        var casted = ExecuteEvents.ValidateEventData<ClientDisconnectedEvent>(data);
        handler.ClientDisconnectedCallback(casted);
    };

    public ulong obj;

    public ClientDisconnectedEvent(EventSystem eventSystem, ulong obj) : base(eventSystem)
    {
        this.obj = obj;
    }
}
