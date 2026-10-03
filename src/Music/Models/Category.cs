namespace Music.Models;

/// <summary>用户自建的曲目分类（一个曲目可以属于多个分类）。</summary>
public sealed class Category
{
    public required string Id { get; init; }

    public required string Name { get; set; }

    public int SortOrder { get; set; }
}
