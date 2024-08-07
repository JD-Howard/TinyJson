using TinyJson.Test;

namespace TinyJson.Test.Models;

public class GenericComplexImp<TValue> : GenericSimpleObject<EnumClass>
{
    public override int Id { get; set; }
    public TValue AltId { get; set; }
    public override EnumClass Obj { get; set; }
}