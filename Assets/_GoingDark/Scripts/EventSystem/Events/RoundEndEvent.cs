using UnityEngine;
using UnityEngine.EventSystems;

public class RoundEndEvent : BaseEventData
{
    public static readonly ExecuteEvents.EventFunction<IEventListener> Delegate = delegate (IEventListener handler, BaseEventData data)
    {
        var casted = ExecuteEvents.ValidateEventData<RoundEndEvent>(data);
        handler.RoundEndCallback(casted);
    };

    public RoundEndEvent(EventSystem eventSystem) : base(eventSystem)
    {

    }
}
