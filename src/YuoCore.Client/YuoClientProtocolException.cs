namespace YuoCore.Client;

public sealed class YuoClientProtocolException : Exception
{
    public YuoClientProtocolException(string code, string message)
        : base(message)
    {
        Code = string.IsNullOrWhiteSpace(code) ? "protocol_error" : code;
    }

    public string Code { get; }
}
