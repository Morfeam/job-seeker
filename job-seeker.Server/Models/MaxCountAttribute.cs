using System.Collections;
using System.ComponentModel.DataAnnotations;

namespace job_seeker.Server.Models;

// Validates that a collection property has at most N elements. Used for the
// keywords/jobTitleExclusionList caps on AddCriteriaRequest.
public class MaxCountAttribute : ValidationAttribute
{
    private readonly int _max;

    public MaxCountAttribute(int max) : base($"This field accepts at most {max} entries.")
    {
        _max = max;
    }

    public override bool IsValid(object? value) =>
        value is not ICollection collection || collection.Count <= _max;
}
