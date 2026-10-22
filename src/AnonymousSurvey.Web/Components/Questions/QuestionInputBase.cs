using AnonymousSurvey.Application.Dtos;
using AnonymousSurvey.Web.Models;
using Microsoft.AspNetCore.Components;

namespace AnonymousSurvey.Web.Components.Questions;

/// <summary>
/// Wspólna baza komponentów pytań; typ pytania wybiera konkretną implementację (polimorfizm dynamiczny).
/// </summary>
public abstract class QuestionInputBase : ComponentBase
{
    [Parameter, EditorRequired] public SurveyQuestionDto Question { get; set; } = null!;

    [Parameter, EditorRequired] public AnswerFormModel Model { get; set; } = null!;

    [Parameter] public int Number { get; set; }

    [Parameter] public string? Error { get; set; }

    [Parameter] public EventCallback OnAnswerChanged { get; set; }

    protected Task NotifyChangedAsync() => OnAnswerChanged.InvokeAsync();
}
