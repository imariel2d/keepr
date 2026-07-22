namespace Keepr.Api.Domain;

/// <summary>
/// A node in the owner's folder tree. Pure adjacency list: <see cref="ParentId"/> is the only
/// representation of the hierarchy, so a move is a one-row update and there is nothing to keep
/// in sync. Subtree and ancestor walks use recursive CTEs (see FolderService).
///
/// Folders are metadata only — they never appear in an object's storage key, so moving or
/// renaming one costs zero R2 calls. See docs/folder-hierarchy-design.md (FD1, Q-H).
/// </summary>
public class Folder
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OwnerId { get; set; }
    public User? Owner { get; set; }

    /// <summary>Null = top level. Restrict on delete: subtrees are drained by app code, not the DB.</summary>
    public Guid? ParentId { get; set; }
    public Folder? Parent { get; set; }
    public ICollection<Folder> Children { get; set; } = new List<Folder>();
    public ICollection<MediaFile> Files { get; set; } = new List<MediaFile>();

    public string Name { get; set; } = default!;

    /// <summary>
    /// Lowercased <see cref="Name"/>. Target of the sibling-uniqueness index, so name comparison
    /// is case-insensitive without a citext extension. Always set via <see cref="SetName"/>.
    /// </summary>
    public string NameLower { get; set; } = default!;

    /// <summary>Null = live. Set = in trash, and the start of the retention clock.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>
    /// The item whose deletion put this row in the trash; equals <see cref="Id"/> when the user
    /// trashed this folder directly. Restoring only revives rows sharing the restored item's id,
    /// so a folder trashed on its own stays trashed when an ancestor is later restored.
    /// </summary>
    public Guid? DeletedRootId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Keeps <see cref="NameLower"/> in lockstep with <see cref="Name"/>.</summary>
    public void SetName(string name)
    {
        Name = name;
        NameLower = name.ToLowerInvariant();
    }
}
