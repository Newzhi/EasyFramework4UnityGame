namespace BaseFramework.BaseEventSys
{

//事件都需要继承这个结构来实现
    public interface IGameEvent
    {
    
    }

    public class TestEvent: IGameEvent
    {
        public int A = 10;
    }

}