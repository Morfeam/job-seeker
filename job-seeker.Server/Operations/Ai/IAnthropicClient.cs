namespace job_seeker.Server.Operations.Ai;

public interface IAnthropicClient
{
    Task<string> GenerateCoverLetterAsync(
        string jobTitle,
        string company,
        string jobDescription,
        string resumeFileName,
        byte[] resumeContent,
        IReadOnlyList<string> styleExamples,
        CancellationToken cancellationToken = default);
}
