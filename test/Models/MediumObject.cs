using System.Collections.Generic;

namespace TinyJson.Test.Models;

public class MediumObject
{
    public MediumObject A;
    public List<int> B;
    public string C { get; set; }

    // Should not serialize
    private int D = 333;
    public static int E = 555;
    internal int F = 777;
    protected int G = 999;
    public const int H = 111;
}