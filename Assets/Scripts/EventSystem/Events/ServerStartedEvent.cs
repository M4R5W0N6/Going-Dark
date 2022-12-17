using UnityEngine;
using UnityEngine.EventSystems;

public class ServerStartedEvent : BaseEventData
{
    public static readonly ExecuteEvents.EventFunction<IEventListener> Delegate = delegate (IEventListener handler, BaseEventData data)
    {
        var casted = ExecuteEvents.ValidateEventData<ServerStartedEvent>(data);
        handler.ServerStartedCallback(casted);
    };

    public ServerStartedEvent(EventSystem eventSystem) : base(eventSystem)
    {

    }
}
