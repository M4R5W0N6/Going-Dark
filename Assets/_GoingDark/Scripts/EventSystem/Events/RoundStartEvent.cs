using UnityEngine;
using UnityEngine.EventSystems;

public class RoundStartEvent : BaseEventData
{
    public static readonly ExecuteEvents.EventFunction<IEventListener> Delegate = delegate (IEventListener handler, BaseEventData data)
    {
        var casted = ExecuteEvents.ValidateEventData<RoundStartEvent>(data);
        handler.RoundStartCallback(casted);
    };

    public RoundStartEvent(EventSystem eventSystem) : base(eventSystem)
    {

    }
}
