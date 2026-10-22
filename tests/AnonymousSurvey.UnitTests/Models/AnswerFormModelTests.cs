using AnonymousSurvey.Application.Dtos;
using AnonymousSurvey.Domain.Enums;
using AnonymousSurvey.Web.Models;

namespace AnonymousSurvey.UnitTests.Models;

public class AnswerFormModelTests
{
    [Fact]
    public void Carry_over_keeps_answers_for_questions_still_present()
    {
        var scale = new AnswerFormModel(1) { ScaleValue = 4 };
        var yesNo = new AnswerFormModel(2) { BoolValue = true };
        var current = new Dictionary<int, AnswerFormModel>
        {
            [1] = scale,
            [2] = yesNo
        };
        var questions = new SurveyQuestionDto[]
        {
            new(1, "Ocena", QuestionType.Scale, 1, IsRequired: true),
            new(2, "Polecasz?", QuestionType.YesNo, 2, IsRequired: false),
            new(3, "Komentarz", QuestionType.Open, 3, IsRequired: true)
        };

        var next = AnswerFormModel.CarryOver(current, questions);

        Assert.Same(scale, next[1]);
        Assert.Equal(4, next[1].ScaleValue);
        Assert.Same(yesNo, next[2]);
        Assert.Equal(true, next[2].BoolValue);
        Assert.Equal(3, next[3].QuestionId);
        Assert.Null(next[3].ScaleValue);
        Assert.Null(next[3].BoolValue);
        Assert.Null(next[3].TextValue);
    }

    [Fact]
    public void Carry_over_drops_removed_questions()
    {
        var removed = new AnswerFormModel(9) { TextValue = "stara odpowiedź" };
        var kept = new AnswerFormModel(1) { ScaleValue = 2 };
        var current = new Dictionary<int, AnswerFormModel>
        {
            [9] = removed,
            [1] = kept
        };

        var next = AnswerFormModel.CarryOver(current, [new SurveyQuestionDto(1, "Ocena", QuestionType.Scale, 1)]);

        Assert.False(next.ContainsKey(9));
        Assert.Same(kept, next[1]);
        Assert.Equal(2, next[1].ScaleValue);
        Assert.Equal("stara odpowiedź", removed.TextValue);
    }

    [Fact]
    public void Clear_resets_scale_and_yes_no()
    {
        var scale = new AnswerFormModel(1) { ScaleValue = 5, TextValue = "nie powinno zostać" };
        scale.Clear();
        Assert.Null(scale.ScaleValue);
        Assert.Null(scale.BoolValue);
        Assert.Null(scale.TextValue);
        Assert.Equal(1, scale.QuestionId);

        var yesNo = new AnswerFormModel(2) { BoolValue = false };
        yesNo.Clear();
        Assert.Null(yesNo.BoolValue);
        Assert.Null(yesNo.ScaleValue);
    }

    [Fact]
    public void Shape_changed_when_question_removed_or_required_flag_flips()
    {
        var displayed = new SurveyQuestionDto[]
        {
            new(1, "Ocena", QuestionType.Scale, 1, IsRequired: false),
            new(2, "Komentarz", QuestionType.Open, 2, IsRequired: false)
        };

        Assert.True(AnswerFormModel.ShapeChanged(displayed, [displayed[0]]));
        Assert.True(AnswerFormModel.ShapeChanged(
            displayed,
            [displayed[0], displayed[1] with { IsRequired = true }]));
        Assert.False(AnswerFormModel.ShapeChanged(displayed, [displayed[1], displayed[0]]));
    }
}
