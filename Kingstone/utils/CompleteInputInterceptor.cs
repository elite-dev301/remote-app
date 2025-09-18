public class CompleteInputInterceptor
{
    private CompleteKeyInterceptor keyInterceptor;
    private CompleteMouseInterceptor mouseInterceptor;

    public event Action<int, bool, bool> KeyEvent;
    public event Action<CompleteMouseInterceptor.MouseEventInfo> MouseEvent;

    public void StartIntercepting()
    {
        keyInterceptor = new CompleteKeyInterceptor();
        // mouseInterceptor = new CompleteMouseInterceptor();

        keyInterceptor.KeyEvent += (vk, down, sys) => KeyEvent?.Invoke(vk, down, sys);
        // mouseInterceptor.MouseEvent += (info) => MouseEvent?.Invoke(info);

        keyInterceptor.StartIntercepting();
        // mouseInterceptor.StartIntercepting();
    }

    public void StopIntercepting()
    {
        keyInterceptor?.StopIntercepting();
        // mouseInterceptor?.StopIntercepting();
    }
}