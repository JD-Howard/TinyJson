namespace TinyJson.Test.Models;

public abstract class GenericSimpleObject<T>
{
    public abstract int Id { get; set; }
    public abstract T Obj { get; set; }
}