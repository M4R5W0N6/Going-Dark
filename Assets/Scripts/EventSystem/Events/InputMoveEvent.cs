using UnityEngine;
using UnityEngine.EventSystems;

public class InputMoveEvent : BaseEventData
{
    public static readonly ExecuteEvents.EventFunction<IEventListener> Delegate = delegate (IEventListener handler, BaseEventData data)
    {
        var casted = ExecuteEvents.ValidateEventData<InputMoveEvent>(data);
        handler.InputMoveCallback(casted);
    };

    public Vector2 previous, current;

    public InputMoveEvent(EventSystem eventSystem, Vector2 previous, Vector2 current) : base(eventSystem)
    {
        this.previous = previous;
        this.current = current;
    }
}