using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace job_seeker.Server.Operations.Ai;

// Thin, single-purpose client for the Anthropic Messages API — there is exactly one
// consumer (cover-letter generation), so this is not a general-purpose SDK wrapper.
public class AnthropicClient : IAnthropicClient
{
    private const string ApiUrl = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";
    private const string DefaultModel = "claude-sonnet-4-6";
    private const int MaxTokens = 1500;

    private const string SystemPrompt = """
        You are a professional cover letter writer helping a job seeker apply for a specific role.

        You will be given:
        1. Details about a job posting (title, company, and description).
        2. The candidate's resume (as text or as a PDF document).
        3. Optionally, one or more example cover letters the candidate has used before, for tone/style reference only.

        Write a complete, ready-to-send cover letter for this candidate to submit for this specific job. Requirements:

        - Address the specific role and company by name using the details provided. Do not write a generic letter.
        - Draw on concrete, specific details from the candidate's actual resume (skills, past roles, achievements, years of experience) to support why they are a strong fit. Do not invent, assume, or fabricate any experience, skill, employer, or credential that is not present in the resume provided.
        - If the resume does not contain enough relevant detail to make a strong case for some part of the job description, write around that gap honestly rather than fabricating qualifications.
        - Structure the letter in 3-4 paragraphs: an opening that names the role and company and states interest, one or two body paragraphs connecting specific resume details to the job's requirements, and a closing paragraph that reiterates interest and invites next steps.
        - If example cover letters are provided, match their general tone, voice, and structural style, but do not reuse specific job, company, or employer details from them — those examples are for style only, not content.
        - Do not include placeholders such as "[Your Name]", "[Company Address]", "[Date]", or similar bracketed text anywhere in the output. Use the candidate's name only if it appears in the resume; otherwise omit a named signature line entirely rather than inserting a placeholder.
        - Do not include a letterhead, mailing address block, or salutation placeholder. Start directly with a greeting line such as "Dear Hiring Manager," (or a named contact if one is identifiable from the job posting) followed by the body.
        - Output plain text only. Do not use markdown formatting, headers, bullet points, or any special formatting — this text will be saved and downloaded as a plain .txt file.
        - Do not include any commentary, notes, or explanation about the letter itself — output only the letter text.
        """;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;

    public AnthropicClient(HttpClient httpClient, IConfiguration config)
    {
        _httpClient = httpClient;
        _apiKey = config["anthropic-api-key"] ?? throw new InvalidOperationException("Missing required configuration value 'anthropic-api-key'.");
        _model = config["anthropic-cover-letter-model"] ?? DefaultModel;
    }

    public async Task<string> GenerateCoverLetterAsync(
        string jobTitle,
        string company,
        string jobDescription,
        string resumeFileName,
        byte[] resumeContent,
        IReadOnlyList<string> styleExamples,
        CancellationToken cancellationToken = default)
    {
        var contentBlocks = new List<object>
        {
            new
            {
                type = "text",
                text = $"""
                    Job title: {jobTitle}
                    Company: {company}

                    Job description:

                    {jobDescription}

                    Write a cover letter for this candidate applying to this role. The candidate's resume is attached as the next content block.
                    """,
            },
            BuildResumeContentBlock(resumeFileName, resumeContent),
        };

        if (styleExamples.Count > 0)
        {
            contentBlocks.Add(new
            {
                type = "text",
                text = "The following are example(s) of cover letters this candidate has used before. Use them only to match tone, voice, and structure — do not copy specific job/company details from them.",
            });
            foreach (var example in styleExamples)
            {
                contentBlocks.Add(new
                {
                    type = "text",
                    text = $"Example cover letter for style reference:\n\n{example}",
                });
            }
        }

        var requestBody = new
        {
            model = _model,
            max_tokens = MaxTokens,
            system = SystemPrompt,
            messages = new object[]
            {
                new { role = "user", content = contentBlocks },
            },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("x-api-key", _apiKey);
        request.Headers.Add("anthropic-version", AnthropicVersion);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Anthropic API request failed ({(int)response.StatusCode}): {responseBody}");

        var parsed = JsonSerializer.Deserialize<AnthropicResponse>(responseBody, JsonOptions);
        var text = parsed?.Content?.FirstOrDefault(c => c.Type == "text")?.Text;
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Anthropic API returned no cover letter text.");

        return text.Trim();
    }

    private static object BuildResumeContentBlock(string resumeFileName, byte[] resumeContent)
    {
        var extension = Path.GetExtension(resumeFileName).ToLowerInvariant();
        if (extension == ".pdf")
        {
            return new
            {
                type = "document",
                source = new
                {
                    type = "base64",
                    media_type = "application/pdf",
                    data = Convert.ToBase64String(resumeContent),
                },
            };
        }

        // .txt / .md / .html — decode as plain text. (.docx is rejected before reaching this code.)
        return new
        {
            type = "text",
            text = $"Candidate's resume ({resumeFileName}):\n\n{Encoding.UTF8.GetString(resumeContent)}",
        };
    }

    private class AnthropicResponse
    {
        [JsonPropertyName("content")]
        public List<AnthropicContentBlock>? Content { get; set; }
    }

    private class AnthropicContentBlock
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }
}
