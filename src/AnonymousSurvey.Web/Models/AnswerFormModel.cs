using AnonymousSurvey.Application.Dtos;

namespace AnonymousSurvey.Web.Models;

public sealed class AnswerFormModel(int questionId)
{
    public int QuestionId { get; } = questionId;

    public int? ScaleValue { get; set; }

    public bool? BoolValue { get; set; }

    public string? TextValue { get; set; }

    public AnswerInputDto ToDto() => new(QuestionId, ScaleValue, BoolValue, TextValue);
}

public sealed record ScaleChoice(int? Value, string Label);

public sealed record YesNoChoice(bool? Value, string Label);
