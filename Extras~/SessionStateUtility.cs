
namespace IllTaco.Editor
{
	using UnityEditor;

	public abstract class SessionStateObject<T>
	{
		protected readonly string _key;
		protected readonly T _defaultValue;

		public SessionStateObject(string key, T defaultValue)
		{
			_key = key;
			_defaultValue = defaultValue;
		}
		public abstract T Get();
		public abstract void Set(T value);
	}

	public class SessionInt : SessionStateObject<int>
	{
		public override int Get() => SessionState.GetInt(_key, _defaultValue);
		public override void Set(int value) => SessionState.SetInt(_key, value);
		public SessionInt(string key, int defaultValue) : base(key, defaultValue) { }
	}

	public class SessionString : SessionStateObject<string>
	{
		public override string Get() => SessionState.GetString(_key, _defaultValue);
		public override void Set(string value) => SessionState.SetString(_key, value);
		public SessionString(string key, string defaultValue) : base(key, defaultValue) { }
	}
}
