public interface ICookable
{
    CookState CurrentCookState { get; set; }
    void CookInFire(float heat);
}
