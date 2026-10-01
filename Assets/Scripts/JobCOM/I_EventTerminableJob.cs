/// <summary>A job an event may end early (TerminateJob Result).</summary>
public interface I_EventTerminableJob
{
    /// <summary>Ends the job now: release actors and everything it holds.</summary>
    void TerminateByEvent();
}
