namespace job_seeker.Server.Operations.Storage;

public interface ICriteriaKeywordsStorage
{
    Task<List<string>> GetKeywordsAsync(string customerId, string criteriaId);
}
