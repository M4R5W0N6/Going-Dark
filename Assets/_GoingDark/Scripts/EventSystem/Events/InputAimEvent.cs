using UnityEngine;
using UnityEngine.EventSystems;

public class InputAimEvent : BaseEventData
{
    public static readonly ExecuteEvents.EventFunction<IEventListener> Delegate = delegate (IEventListener handler, BaseEventData data)
    {
        var casted = ExecuteEvents.ValidateEventData<InputAimEvent>(data);
        handler.InputAimCallback(casted);
    };

    public bool previous, current;

    public InputAimEvent(EventSystem eventSystem, bool previous, bool current) : base(eventSystem)
    {
        this.previous = previous;
        this.current = current;
    }
}