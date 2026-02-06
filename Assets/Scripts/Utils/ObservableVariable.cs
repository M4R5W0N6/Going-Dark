using System;

namespace LocalData
{
    public class ObservableVariable<T>
    {
        private T _value;

        public ObservableVariable()
        {
            _value = default(T);
        }

        public ObservableVariable(T initialValue)
        {
            _value = initialValue;
        }

        public delegate void OnValueChangedDelegate(T previousValue, T currentValue);
        public event OnValueChangedDelegate OnValueChanged;

        public T Value
        {
            get { return _value; }
            set
            {
                if (Equals(_value, value)) return;
                T previous = _value;
                _value = value;
                var handler = OnValueChanged;
                if (handler != null)
                {
                    handler(previous, _value);
                }
            }
        }

        public void ForceNotify()
        {
            var handler = OnValueChanged;
            if (handler != null)
            {
                handler(_value, _value);
            }
        }
    }
}


