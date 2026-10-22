using AnonymousSurvey.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AnonymousSurvey.Infrastructure.Persistence;

public class SurveyDbContext(DbContextOptions<SurveyDbContext> options) : DbContext(options)
{
    public DbSet<Survey> Surveys => Set<Survey>();

    public DbSet<Question> Questions => Set<Question>();

    public DbSet<SurveyQuestion> SurveyQuestions => Set<SurveyQuestion>();

    public DbSet<ResponseSession> ResponseSessions => Set<ResponseSession>();

    public DbSet<Answer> Answers => Set<Answer>();

    /// <summary>
    /// Mapowanie funkcji tabelarycznej T-SQL dbo.fn_GetSurveyQuestions (jedyna ścieżka odczytu pytań formularza).
    /// </summary>
    public IQueryable<SurveyQuestionRow> GetSurveyQuestions(int surveyId) =>
        FromExpression(() => GetSurveyQuestions(surveyId));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDbFunction(
                typeof(SurveyDbContext).GetMethod(nameof(GetSurveyQuestions), [typeof(int)])!)
            .HasName("fn_GetSurveyQuestions")
            .HasSchema("dbo");

        modelBuilder.Entity<SurveyQuestionRow>().HasNoKey();

        modelBuilder.Entity<Survey>(entity =>
        {
            entity.ToTable("Survey");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Slug).HasMaxLength(60).IsRequired();
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(1000);
            entity.Property(x => x.Status).HasConversion<int>();
            entity.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(0)");
            entity.Property(x => x.UpdatedAtUtc).HasColumnType("datetime2(0)");
            entity.Property(x => x.RowVersion).IsRowVersion();
            entity.HasIndex(x => x.Slug).IsUnique();
            entity.Property(x => x.ResultsAccessToken).HasMaxLength(64).IsRequired();
            entity.Property(x => x.ConfigAccessToken).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => x.ResultsAccessToken).IsUnique();
            entity.HasIndex(x => x.ConfigAccessToken).IsUnique();
        });

        modelBuilder.Entity<Question>(entity =>
        {
            entity.ToTable("Question");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Text).HasMaxLength(500).IsRequired();
            entity.Property(x => x.Type).HasConversion<int>();
            entity.HasIndex(x => new { x.Type, x.Text }).IsUnique().HasDatabaseName("UX_Question_Type_Text");
        });

        modelBuilder.Entity<SurveyQuestion>(entity =>
        {
            entity.ToTable("SurveyQuestion");
            entity.HasKey(x => new { x.SurveyId, x.QuestionId });
            entity.Property(x => x.AddedAtUtc).HasColumnType("datetime2(0)");
            entity.HasOne(x => x.Survey).WithMany(x => x.SurveyQuestions).HasForeignKey(x => x.SurveyId);
            entity.HasOne(x => x.Question).WithMany(x => x.SurveyQuestions).HasForeignKey(x => x.QuestionId);
        });

        modelBuilder.Entity<ResponseSession>(entity =>
        {
            entity.ToTable("ResponseSession");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.SubmittedAtUtc).HasColumnType("datetime2(0)");
            entity.HasOne(x => x.Survey).WithMany(x => x.ResponseSessions).HasForeignKey(x => x.SurveyId);
        });

        modelBuilder.Entity<Answer>(entity =>
        {
            entity.ToTable("Answer");
            entity.HasKey(x => new { x.SessionId, x.QuestionId });
            entity.Property(x => x.TextValue).HasMaxLength(2000);
            entity.HasOne(x => x.Session).WithMany(x => x.Answers).HasForeignKey(x => x.SessionId);
            entity.HasOne(x => x.Question).WithMany(x => x.Answers).HasForeignKey(x => x.QuestionId);
        });
    }
}
