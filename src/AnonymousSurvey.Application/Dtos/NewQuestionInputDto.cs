using AnonymousSurvey.Domain.Enums;

namespace AnonymousSurvey.Application.Dtos;

public sealed record NewQuestionInputDto(string Text, QuestionType Type);
