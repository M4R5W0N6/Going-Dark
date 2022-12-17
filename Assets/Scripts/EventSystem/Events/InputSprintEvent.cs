using UnityEngine;
using UnityEngine.EventSystems;

public class InputSprintEvent : BaseEventData
{
    public static readonly ExecuteEvents.EventFunction<IEventListener> Delegate = delegate (IEventListener handler, BaseEventData data)
    {
        var casted = ExecuteEvents.ValidateEventData<InputSprintEvent>(data);
        handler.InputSprintCallback(casted);
    };

    public bool previous, current;

    public InputSprintEvent(EventSystem eventSystem, bool previous, bool current) : base(eventSystem)
    {
        this.previous = previous;
        this.current = current;
    }
}