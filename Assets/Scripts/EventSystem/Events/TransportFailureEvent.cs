using UnityEngine;
using UnityEngine.EventSystems;

public class TransportFailureEvent : BaseEventData
{
    public static readonly ExecuteEvents.EventFunction<IEventListener> Delegate = delegate (IEventListener handler, BaseEventData data)
    {
        var casted = ExecuteEvents.ValidateEventData<TransportFailureEvent>(data);
        handler.TransportFailureCallback(casted);
    };

    public TransportFailureEvent(EventSystem eventSystem) : base(eventSystem)
    {

    }
}
