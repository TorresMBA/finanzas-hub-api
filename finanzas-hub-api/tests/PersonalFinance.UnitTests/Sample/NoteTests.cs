using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Sample.Domain;

namespace PersonalFinance.UnitTests.Sample;

public class NoteTests
{
    private static readonly Guid Workspace = Guid.NewGuid();
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_note_keeps_its_owner_and_raises_one_created_event()
    {
        var note = Note.Create(Workspace, Owner, "  Comprar pan  ", Now).Value;

        Assert.Equal("Comprar pan", note.Title);
        Assert.Equal(Workspace, note.WorkspaceId);
        Assert.Equal(Owner, note.OwnerUserId);
        var created = Assert.IsType<NoteCreated>(Assert.Single(note.DomainEvents));
        Assert.Equal(new NoteCreated(note.Id, Workspace, Owner), created);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_note_without_a_title_is_rejected(string? title)
    {
        var note = Note.Create(Workspace, Owner, title, Now);

        Assert.True(note.IsFailure);
        Assert.Equal(ErrorType.Validation, note.Error.Type);
        Assert.Equal("sample.note.title_invalid", note.Error.Code);
    }

    [Fact]
    public void A_title_longer_than_the_limit_is_rejected()
    {
        var note = Note.Create(Workspace, Owner, new string('a', Note.MaxTitleLength + 1), Now);

        Assert.True(note.IsFailure);
    }

    [Fact]
    public void Identifiers_are_generated_in_time_order()
    {
        var first = Note.Create(Workspace, Owner, "one", Now).Value;
        var second = Note.Create(Workspace, Owner, "two", Now).Value;

        Assert.Equal(7, first.Id.Version);
        Assert.NotEqual(first.Id, second.Id);
    }
}
