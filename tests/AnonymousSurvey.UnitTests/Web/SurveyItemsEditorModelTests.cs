using AnonymousSurvey.Application.Dtos;
using AnonymousSurvey.Domain.Enums;
using AnonymousSurvey.Web.Models;

namespace AnonymousSurvey.UnitTests.Web;

public class SurveyItemsEditorModelTests
{
    [Fact]
    public void Add_from_pool_moves_item_out_of_available_pool()
    {
        var model = SurveyItemsEditorModel.ForNewSurvey(
        [
            Item(1, "Alfa", QuestionType.Scale),
            Item(2, "Beta", QuestionType.Open)
        ]);

        var result = model.AddFromPool(2, isRequired: false);

        Assert.True(result.IsValid);
        Assert.Equal([2], model.Items.Select(item => item.QuestionId).ToArray());
        Assert.Equal("Beta", model.Items[0].Text);
        Assert.Equal(QuestionType.Open, model.Items[0].Type);
        Assert.False(model.Items[0].IsRequired);
        Assert.False(model.Items[0].IsNew);
        Assert.Equal([1], model.AvailablePool.Select(item => item.QuestionId).ToArray());

        var duplicate = model.AddFromPool(2);
        Assert.False(duplicate.IsValid);
        Assert.Equal("To pytanie jest już w ankiecie.", duplicate.Errors[0]);
        Assert.Single(model.Items);
    }

    [Fact]
    public void Add_new_rejects_empty_and_over_500()
    {
        var model = SurveyItemsEditorModel.ForNewSurvey([]);

        var empty = model.AddNew("", QuestionType.Scale);
        var tooLong = model.AddNew(new string('a', 501), QuestionType.Open);

        Assert.False(empty.IsValid);
        Assert.Equal("Tekst pytania jest wymagany.", empty.Errors[0]);
        Assert.False(tooLong.IsValid);
        Assert.Equal("Tekst pytania może mieć maksymalnie 500 znaków.", tooLong.Errors[0]);
        Assert.Empty(model.Items);
    }

    [Fact]
    public void Add_new_rejects_duplicate_of_pool_and_of_list()
    {
        var model = SurveyItemsEditorModel.ForNewSurvey(
        [
            Item(1, "Obsługa", QuestionType.Scale)
        ]);

        var fromPool = model.AddNew("  obsługa  ", QuestionType.Scale);
        var added = model.AddNew("Nowe pytanie", QuestionType.Open);
        var fromList = model.AddNew("nowe pytanie", QuestionType.Open);

        Assert.False(fromPool.IsValid);
        Assert.Equal("Pytanie „obsługa” już istnieje w puli — dodaj je z listy.", fromPool.Errors[0]);
        Assert.True(added.IsValid);
        Assert.False(fromList.IsValid);
        Assert.Equal("Pytanie „nowe pytanie” już istnieje w puli — dodaj je z listy.", fromList.Errors[0]);
        Assert.Single(model.Items);
    }

    [Fact]
    public void Add_new_appears_in_list_but_not_in_pool()
    {
        var model = SurveyItemsEditorModel.ForNewSurvey([Item(1, "Z puli", QuestionType.YesNo)]);

        var result = model.AddNew("Całkiem nowe", QuestionType.YesNo, isRequired: false);

        Assert.True(result.IsValid);
        Assert.True(model.Items[0].IsNew);
        Assert.Null(model.Items[0].QuestionId);
        Assert.Equal("Całkiem nowe", model.Items[0].Text);
        Assert.Equal(QuestionType.YesNo, model.Items[0].Type);
        Assert.False(model.Items[0].IsRequired);
        Assert.Equal(0, model.Items[0].AnswerCount);
        Assert.DoesNotContain(model.AvailablePool, item => item.Text == "Całkiem nowe");
        Assert.Equal([1], model.AvailablePool.Select(item => item.QuestionId).ToArray());
    }

    [Fact]
    public void Remove_returns_pool_item_to_available_pool_but_drops_new_item()
    {
        var model = SurveyItemsEditorModel.ForNewSurvey(
        [
            Item(1, "Z puli", QuestionType.Scale),
            Item(2, "Zostaje", QuestionType.Open)
        ]);
        model.AddFromPool(1);
        model.AddNew("Tylko na liście", QuestionType.Open);

        model.Remove(1);
        model.Remove(0);

        Assert.Empty(model.Items);
        Assert.Equal([1, 2], model.AvailablePool.Select(item => item.QuestionId).ToArray());
        Assert.DoesNotContain(model.AvailablePool, item => item.Text == "Tylko na liście");
    }

    [Fact]
    public void Move_respects_bounds()
    {
        var model = SurveyItemsEditorModel.ForNewSurvey(Pool(3));
        model.AddFromPool(1);
        model.AddFromPool(2);
        model.AddFromPool(3);

        model.Move(2, -1);
        Assert.Equal([1, 3, 2], Ids(model));

        model.Move(0, -5);
        model.Move(1, 0);
        model.Move(8, 1);
        Assert.Equal([1, 3, 2], Ids(model));

        model.Move(0, 1);
        Assert.Equal([3, 1, 2], Ids(model));
    }

    [Fact]
    public void ToInputs_orders_by_list_position_and_maps_new_vs_existing()
    {
        var model = SurveyItemsEditorModel.FromConfig(Config(
            assigned:
            [
                Assigned(1, "Pierwsze", QuestionType.Scale, sort: 1, required: true, answers: 4)
            ],
            pool:
            [
                Item(1, "Pierwsze", QuestionType.Scale),
                Item(2, "Drugie", QuestionType.YesNo)
            ]));
        model.AddFromPool(2, isRequired: false);
        model.AddNew("Trzecie", QuestionType.Open);
        model.Move(2, -1);
        model.Move(1, -1);

        var inputs = model.ToInputs();

        Assert.Equal(3, inputs.Count);
        var created = inputs[0].NewQuestion;
        Assert.Null(inputs[0].QuestionId);
        Assert.NotNull(created);
        Assert.Equal("Trzecie", created.Text);
        Assert.Equal(QuestionType.Open, created.Type);
        Assert.True(inputs[0].IsRequired);

        Assert.Equal(1, inputs[1].QuestionId);
        Assert.Null(inputs[1].NewQuestion);
        Assert.True(inputs[1].IsRequired);

        Assert.Equal(2, inputs[2].QuestionId);
        Assert.Null(inputs[2].NewQuestion);
        Assert.False(inputs[2].IsRequired);
    }

    [Fact]
    public void IsDirty_and_Reset()
    {
        var model = SurveyItemsEditorModel.FromConfig(Config(
            assigned:
            [
                Assigned(1, "Pierwsze", QuestionType.Scale, sort: 1, required: true),
                Assigned(2, "Drugie", QuestionType.Open, sort: 2, required: false)
            ],
            pool:
            [
                Item(1, "Pierwsze", QuestionType.Scale),
                Item(2, "Drugie", QuestionType.Open),
                Item(3, "Trzecie", QuestionType.YesNo)
            ]));

        Assert.False(model.IsDirty);

        model.Items[0].IsRequired = false;
        Assert.True(model.IsDirty);
        model.Reset();
        Assert.False(model.IsDirty);
        Assert.True(model.Items[0].IsRequired);

        Assert.True(model.AddFromPool(3).IsValid);
        Assert.True(model.IsDirty);
        model.Reset();
        Assert.False(model.IsDirty);
        Assert.Equal([1, 2], Ids(model));

        model.Move(0, 1);
        Assert.True(model.IsDirty);
        model.Reset();
        Assert.Equal([1, 2], Ids(model));

        model.Remove(0);
        Assert.True(model.IsDirty);
        model.Reset();
        Assert.False(model.IsDirty);
        Assert.Equal([1, 2], Ids(model));
    }

    [Fact]
    public void FromConfig_loads_required_flags_and_answer_counts()
    {
        var version = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var model = SurveyItemsEditorModel.FromConfig(Config(
            responseCount: 7,
            rowVersion: version,
            assigned:
            [
                Assigned(2, "Drugie", QuestionType.Open, sort: 2, required: false, answers: 4),
                Assigned(1, "Pierwsze", QuestionType.Scale, sort: 1, required: true, answers: 0)
            ],
            pool:
            [
                Item(1, "Pierwsze", QuestionType.Scale),
                Item(2, "Drugie", QuestionType.Open),
                Item(3, "Wolne", QuestionType.YesNo)
            ]));

        Assert.Equal(7, model.ResponseCount);
        Assert.Equal(version, model.RowVersion);
        Assert.False(model.IsDirty);
        Assert.Equal([1, 2], Ids(model));
        Assert.True(model.Items[0].IsRequired);
        Assert.Equal(0, model.Items[0].AnswerCount);
        Assert.False(model.Items[1].IsRequired);
        Assert.Equal(4, model.Items[1].AnswerCount);
        Assert.False(model.Items[0].IsNew);
        Assert.Equal([3], model.AvailablePool.Select(item => item.QuestionId).ToArray());
        Assert.Empty(SurveyItemsEditorModel.ForNewSurvey([]).RowVersion);
        Assert.Equal(0, SurveyItemsEditorModel.ForNewSurvey([]).ResponseCount);
    }

    [Fact]
    public void Add_rejects_31st_item_from_pool_and_new()
    {
        var model = SurveyItemsEditorModel.ForNewSurvey(Pool(31));
        for (var id = 1; id <= 30; id++)
        {
            Assert.True(model.AddFromPool(id).IsValid);
        }

        Assert.False(model.CanAddMore);

        var fromPool = model.AddFromPool(31);
        var fromNew = model.AddNew("Nadmiar", QuestionType.Scale);

        Assert.False(fromPool.IsValid);
        Assert.Equal("Ankieta może zawierać maksymalnie 30 pytań.", fromPool.Errors[0]);
        Assert.False(fromNew.IsValid);
        Assert.Equal("Ankieta może zawierać maksymalnie 30 pytań.", fromNew.Errors[0]);
        Assert.Equal(30, model.Items.Count);
        Assert.Equal(31, model.AvailablePool.Single().QuestionId);
    }

    [Fact]
    public void Pool_filter_is_case_insensitive_and_trimmed()
    {
        var model = SurveyItemsEditorModel.ForNewSurvey(
        [
            Item(1, "Jak oceniasz czytelność naszej strony?", QuestionType.Scale),
            Item(2, "Czy poleciłbyś nas znajomym?", QuestionType.YesNo)
        ]);

        model.PoolFilter = "  CZYTELNOŚĆ ";

        Assert.Equal([1], model.AvailablePool.Select(item => item.QuestionId).ToArray());
        Assert.False(model.IsDirty);

        model.PoolFilter = "   ";

        Assert.Equal([1, 2], model.AvailablePool.Select(item => item.QuestionId).ToArray());
    }

    [Fact]
    public void Add_new_text_boundaries_1_500_501()
    {
        var model = SurveyItemsEditorModel.ForNewSurvey([]);

        Assert.True(model.AddNew("a", QuestionType.Open).IsValid);
        Assert.True(model.AddNew(new string('b', 500), QuestionType.Scale).IsValid);
        var rejected = model.AddNew(new string('c', 501), QuestionType.YesNo);

        Assert.False(rejected.IsValid);
        Assert.Equal("Tekst pytania może mieć maksymalnie 500 znaków.", rejected.Errors[0]);
        Assert.Equal(2, model.Items.Count);
        Assert.Equal(1, model.Items[0].Text.Length);
        Assert.Equal(500, model.Items[1].Text.Length);
    }

    [Fact]
    public void Add_new_trims_whitespace_and_rejects_whitespace_only()
    {
        var model = SurveyItemsEditorModel.ForNewSurvey([]);

        var blank = model.AddNew(" \t  ", QuestionType.Open);
        var trimmed = model.AddNew("  Witaj świecie  ", QuestionType.Open);

        Assert.False(blank.IsValid);
        Assert.Equal("Tekst pytania jest wymagany.", blank.Errors[0]);
        Assert.True(trimmed.IsValid);
        Assert.Equal("Witaj świecie", model.Items.Single().Text);
    }

    [Fact]
    public void Duplicate_check_is_per_type()
    {
        var model = SurveyItemsEditorModel.ForNewSurvey(
        [
            Item(1, "To samo", QuestionType.Scale)
        ]);

        var otherType = model.AddNew("to samo", QuestionType.Open);
        var sameType = model.AddNew("TO SAMO", QuestionType.Scale);

        Assert.True(otherType.IsValid);
        Assert.False(sameType.IsValid);
        Assert.Equal("Pytanie „TO SAMO” już istnieje w puli — dodaj je z listy.", sameType.Errors[0]);
        Assert.Single(model.Items);
        Assert.Equal(QuestionType.Open, model.Items[0].Type);
    }

    [Fact]
    public void Move_first_up_and_last_down_are_noops()
    {
        var model = SurveyItemsEditorModel.FromConfig(Config(
            assigned:
            [
                Assigned(1, "Pierwsze", QuestionType.Scale, sort: 1, required: true),
                Assigned(2, "Drugie", QuestionType.Open, sort: 2, required: true)
            ],
            pool:
            [
                Item(1, "Pierwsze", QuestionType.Scale),
                Item(2, "Drugie", QuestionType.Open)
            ]));

        model.Move(0, -1);
        model.Move(1, 1);

        Assert.Equal([1, 2], Ids(model));
        Assert.False(model.IsDirty);
    }

    [Fact]
    public void Remove_last_item_then_ToInputs_is_empty()
    {
        var model = SurveyItemsEditorModel.ForNewSurvey([Item(1, "Jedno", QuestionType.Scale)]);
        model.AddFromPool(1);

        model.Remove(0);

        Assert.Empty(model.Items);
        Assert.Empty(model.ToInputs());
    }

    [Fact]
    public void Reset_restores_pool_and_required_flags()
    {
        var model = SurveyItemsEditorModel.FromConfig(Config(
            assigned:
            [
                Assigned(1, "Pierwsze", QuestionType.Scale, sort: 1, required: true, answers: 3),
                Assigned(2, "Drugie", QuestionType.Open, sort: 2, required: false, answers: 1)
            ],
            pool:
            [
                Item(1, "Pierwsze", QuestionType.Scale),
                Item(2, "Drugie", QuestionType.Open),
                Item(3, "Trzecie", QuestionType.YesNo)
            ]));

        model.Items[0].IsRequired = false;
        model.Items[1].IsRequired = true;
        Assert.True(model.AddFromPool(3).IsValid);
        model.AddNew("Nowe", QuestionType.Open);
        model.Remove(0);
        model.PoolFilter = "trzecie";

        model.Reset();

        Assert.Equal("", model.PoolFilter);
        Assert.False(model.IsDirty);
        Assert.Equal([1, 2], Ids(model));
        Assert.True(model.Items[0].IsRequired);
        Assert.Equal(3, model.Items[0].AnswerCount);
        Assert.False(model.Items[1].IsRequired);
        Assert.Equal(1, model.Items[1].AnswerCount);
        Assert.Equal([3], model.AvailablePool.Select(item => item.QuestionId).ToArray());
        Assert.DoesNotContain(model.AvailablePool, item => item.Text == "Nowe");
    }

    [Fact]
    public void ToInputs_roundtrip_keeps_row_version()
    {
        var version = new byte[] { 9, 8, 7, 6, 5, 4, 3, 2 };
        var model = SurveyItemsEditorModel.FromConfig(Config(
            rowVersion: version,
            assigned: [Assigned(1, "Pierwsze", QuestionType.Scale, sort: 1, required: true)],
            pool: [Item(1, "Pierwsze", QuestionType.Scale), Item(2, "Drugie", QuestionType.Open)]));

        var before = model.ToInputs();
        version[0] = 0;
        model.Items[0].IsRequired = false;
        Assert.True(model.AddFromPool(2).IsValid);
        var after = model.ToInputs();

        Assert.Equal(new byte[] { 9, 8, 7, 6, 5, 4, 3, 2 }, model.RowVersion);
        Assert.NotSame(version, model.RowVersion);
        Assert.True(before[0].IsRequired);
        Assert.False(after[0].IsRequired);
        Assert.Equal(2, after[1].QuestionId);
        Assert.Empty(SurveyItemsEditorModel.ForNewSurvey([Item(2, "Drugie", QuestionType.Open)]).RowVersion);
    }

    private static int?[] Ids(SurveyItemsEditorModel model) =>
        model.Items.Select(item => item.QuestionId).ToArray();

    private static List<QuestionPoolItemDto> Pool(int count) =>
        Enumerable.Range(1, count)
            .Select(id => Item(id, $"Pytanie {id}", QuestionType.Open))
            .ToList();

    private static QuestionPoolItemDto Item(int id, string text, QuestionType type) =>
        new(id, text, type, true);

    private static SurveyConfigQuestionDto Assigned(
        int id,
        string text,
        QuestionType type,
        int sort,
        bool required,
        int answers = 0) =>
        new(id, text, type, sort, required, answers);

    private static SurveyConfigDto Config(
        IReadOnlyList<SurveyConfigQuestionDto> assigned,
        IReadOnlyList<QuestionPoolItemDto> pool,
        int responseCount = 0,
        byte[]? rowVersion = null) =>
        new(
            10,
            "ankieta",
            "Tytuł",
            null,
            SurveyStatus.Active,
            false,
            responseCount,
            rowVersion ?? [1, 2, 3, 4, 5, 6, 7, 8],
            assigned,
            pool);
}
