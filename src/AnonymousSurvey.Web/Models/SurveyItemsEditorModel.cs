using AnonymousSurvey.Application.Common;
using AnonymousSurvey.Application.Dtos;
using AnonymousSurvey.Domain.Enums;

namespace AnonymousSurvey.Web.Models;

public sealed class EditableSurveyItem
{
    public int? QuestionId { get; init; }

    public string Text { get; init; } = "";

    public QuestionType Type { get; init; }

    public bool IsRequired { get; set; } = true;

    public int AnswerCount { get; init; }

    public bool IsNew => QuestionId is null;
}

public sealed class SurveyItemsEditorModel
{
    public const string AlreadyOnList = "To pytanie jest już w ankiecie.";
    public const string QuestionRequired = "Tekst pytania jest wymagany.";
    public const string QuestionTooLong = "Tekst pytania może mieć maksymalnie 500 znaków.";
    public const string TooManyItems = "Ankieta może zawierać maksymalnie 30 pytań.";

    private readonly List<QuestionPoolItemDto> _pool;
    private readonly List<EditableSurveyItem> _items = [];
    private List<ItemSnapshot> _snapshot = [];

    private SurveyItemsEditorModel(IEnumerable<QuestionPoolItemDto> pool, int responseCount, byte[] rowVersion)
    {
        _pool = pool.ToList();
        ResponseCount = responseCount;
        RowVersion = rowVersion.ToArray();
    }

    public IReadOnlyList<EditableSurveyItem> Items => _items;

    public IReadOnlyList<QuestionPoolItemDto> AvailablePool
    {
        get
        {
            var used = _items
                .Select(item => item.QuestionId)
                .OfType<int>()
                .ToHashSet();
            var filter = PoolFilter.Trim();

            return _pool
                .Where(item => !used.Contains(item.QuestionId))
                .Where(item => filter.Length == 0 || item.Text.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    public string PoolFilter { get; set; } = "";

    public bool CanAddMore => _items.Count < SurveyLimits.MaxItemsPerSurvey;

    public byte[] RowVersion { get; }

    public int ResponseCount { get; }

    public bool IsDirty => !SameAsSnapshot();

    public static SurveyItemsEditorModel ForNewSurvey(IReadOnlyList<QuestionPoolItemDto> pool)
    {
        var model = new SurveyItemsEditorModel(pool, responseCount: 0, rowVersion: []);
        model.Capture();
        return model;
    }

    public static SurveyItemsEditorModel FromConfig(SurveyConfigDto config)
    {
        var model = new SurveyItemsEditorModel(config.QuestionPool, config.ResponseCount, config.RowVersion);
        foreach (var question in config.AssignedQuestions.OrderBy(question => question.SortOrder))
        {
            model._items.Add(new EditableSurveyItem
            {
                QuestionId = question.QuestionId,
                Text = question.Text,
                Type = question.Type,
                IsRequired = question.IsRequired,
                AnswerCount = question.AnswerCount
            });
        }

        model.Capture();
        return model;
    }

    public ValidationResult AddFromPool(int questionId, bool isRequired = true)
    {
        if (_items.Any(item => item.QuestionId == questionId))
        {
            return ValidationResult.Failure(AlreadyOnList);
        }

        var poolItem = _pool.FirstOrDefault(item => item.QuestionId == questionId);
        if (poolItem is null)
        {
            return ValidationResult.Failure($"Nieznane pytanie (Id {questionId}).");
        }

        if (!CanAddMore)
        {
            return ValidationResult.Failure(TooManyItems);
        }

        _items.Add(new EditableSurveyItem
        {
            QuestionId = poolItem.QuestionId,
            Text = poolItem.Text,
            Type = poolItem.Type,
            IsRequired = isRequired
        });
        return ValidationResult.Success();
    }

    public ValidationResult AddNew(string text, QuestionType type, bool isRequired = true)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return ValidationResult.Failure(QuestionRequired);
        }

        if (trimmed.Length > SurveyLimits.MaxQuestionTextLength)
        {
            return ValidationResult.Failure(QuestionTooLong);
        }

        if (!CanAddMore)
        {
            return ValidationResult.Failure(TooManyItems);
        }

        if (Duplicates(trimmed, type))
        {
            return ValidationResult.Failure($"Pytanie „{trimmed}” już istnieje w puli — dodaj je z listy.");
        }

        _items.Add(new EditableSurveyItem
        {
            Text = trimmed,
            Type = type,
            IsRequired = isRequired
        });
        return ValidationResult.Success();
    }

    public void Remove(int index)
    {
        if (index < 0 || index >= _items.Count)
        {
            return;
        }

        _items.RemoveAt(index);
    }

    public void Move(int index, int delta)
    {
        if (delta is not (-1 or 1))
        {
            return;
        }

        var target = index + delta;
        if (index < 0 || target < 0 || index >= _items.Count || target >= _items.Count)
        {
            return;
        }

        (_items[index], _items[target]) = (_items[target], _items[index]);
    }

    public IReadOnlyList<SurveyItemInputDto> ToInputs() =>
        _items.Select(item => item.QuestionId is int questionId
                ? new SurveyItemInputDto(questionId, null, item.IsRequired)
                : new SurveyItemInputDto(null, new NewQuestionInputDto(item.Text, item.Type), item.IsRequired))
            .ToList();

    public void Reset()
    {
        _items.Clear();
        _items.AddRange(_snapshot.Select(snapshot => snapshot.Restore()));
        PoolFilter = "";
    }

    private bool Duplicates(string text, QuestionType type) =>
        _items.Any(item => item.Type == type && string.Equals(item.Text.Trim(), text, StringComparison.OrdinalIgnoreCase))
        || _pool.Any(item => item.Type == type && string.Equals(item.Text.Trim(), text, StringComparison.OrdinalIgnoreCase));

    private void Capture() => _snapshot = _items.Select(ItemSnapshot.From).ToList();

    private bool SameAsSnapshot()
    {
        if (_items.Count != _snapshot.Count)
        {
            return false;
        }

        for (var index = 0; index < _items.Count; index++)
        {
            if (!_snapshot[index].Matches(_items[index]))
            {
                return false;
            }
        }

        return true;
    }

    private readonly record struct ItemSnapshot(
        int? QuestionId,
        string Text,
        QuestionType Type,
        bool IsRequired,
        int AnswerCount)
    {
        public static ItemSnapshot From(EditableSurveyItem item) =>
            new(item.QuestionId, item.Text, item.Type, item.IsRequired, item.AnswerCount);

        public bool Matches(EditableSurveyItem item) =>
            QuestionId == item.QuestionId
            && Type == item.Type
            && IsRequired == item.IsRequired
            && AnswerCount == item.AnswerCount
            && string.Equals(Text, item.Text, StringComparison.Ordinal);

        public EditableSurveyItem Restore() => new()
        {
            QuestionId = QuestionId,
            Text = Text,
            Type = Type,
            IsRequired = IsRequired,
            AnswerCount = AnswerCount
        };
    }
}
