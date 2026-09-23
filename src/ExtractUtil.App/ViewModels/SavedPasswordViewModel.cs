namespace ExtractUtil.App.ViewModels;

public sealed class SavedPasswordViewModel
{
    public SavedPasswordViewModel(string value)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public string Value { get; }

    public string MaskedValue
    {
        get
        {
            var visibleLength = Math.Clamp(Value.Length, 4, 12);
            return $"{new string('●', visibleLength)}  （{Value.Length} 位）";
        }
    }
}
