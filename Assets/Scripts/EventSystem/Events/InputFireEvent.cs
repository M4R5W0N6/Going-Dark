using UnityEngine;
using UnityEngine.EventSystems;

public class InputFireEvent : BaseEventData
{
    public static readonly ExecuteEvents.EventFunction<IEventListener> Delegate = delegate (IEventListener handler, BaseEventData data)
    {
        var casted = ExecuteEvents.ValidateEventData<InputFireEvent>(data);
        handler.InputFireCallback(casted);
    };

    public bool previous, current;

    public InputFireEvent(EventSystem eventSystem, bool previous, bool current) : base(eventSystem)
    {
        this.previous = previous;
        this.current = current;
    }
}