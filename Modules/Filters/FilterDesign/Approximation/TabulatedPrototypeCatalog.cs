using System.Globalization;
using System.Numerics;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Approximation;

// Pole coordinates are from Analog Devices AN-649 Tables IX-XII. Positive real
// coordinates in the tables denote damping; poles are stored in the LHP here.
public static class TabulatedPrototypeCatalog
{
    private static readonly IReadOnlyDictionary<FilterApproximation, IReadOnlyDictionary<int, Complex[]>> Tables =
        new Dictionary<FilterApproximation, IReadOnlyDictionary<int, Complex[]>>
        {
            [FilterApproximation.LinearPhase005Degree] = Parse("2:1.0087,.6680|3:.8541,1.0725;1.0459,0|4:.9648,.4748;.7448,1.4008|5:.8915,.8733;.6731,1.7085;.9430,0|6:.8904,.4111;.8233,1.2179;.6152,1.9810|7:.8425,.7791;.7708,1.5351;.5727,2.2456;.8615,0|8:.8195,.3711;.7930,1.1054;.7213,1.8134;.5341,2.4761|9:.7853,.7125;.7555,1.4127;.6849,2.0854;.5060,2.7133;.7983,0|10:.7592,.3413;.7467,1.0195;.7159,1.6836;.6475,2.3198;.4777,2.9128"),
            [FilterApproximation.LinearPhase05Degree] = Parse("2:.8590,.6981|3:.6969,1.1318;.8257,0|4:.7448,.5133;.6037,1.4983|5:.6675,.9401;.5412,1.8256;.7056,0|6:.6519,.4374;.6167,1.2963;.4893,2.0982|7:.6190,.8338;.5816,1.6455;.4598,2.3994;.6283,0|8:.5791,.3857;.5665,1.1505;.5303,1.8914;.4148,2.5780|9:.5688,.7595;.5545,1.5089;.5179,2.2329;.4080,2.9028;.5728,0|10:.5249,.3487;.5193,1.0429;.5051,1.7264;.4711,2.3850;.3708,2.9940"),
            [FilterApproximation.GaussianTo12Db] = Parse("3:.9622,1.2214;.9776,.5029|4:.7940,.5029;.6304,1.5407|5:.6190,.8254;.3559,1.5688;.6650,0|6:.5433,.3431;.4672,.9991;.2204,1.5067|7:.4580,.5932;.3649,1.1286;.1522,1.4938;.4828,0|8:.4222,.2640;.3833,.7716;.2678,1.2066;.1122,1.4798|9:.3700,.4704;.3230,.9068;.2309,1.2634;.0860,1.4740;.3842,0|10:.3384,.2101;.3164,.6180;.2677,.9852;.1848,1.2745;.0671,1.4389"),
            [FilterApproximation.GaussianTo6Db] = Parse("3:.9360,1.2168;.9360,0|4:.9278,1.6995;.9192,.5560|5:.8075,.9973;.7153,.2053;.8131,0|6:.7019,.4322;.6667,1.2931;.4479,2.1363|7:.6155,.7703;.5486,1.5154;.2905,2.1486;.6291,0|8:.5441,.3358;.5175,.9962;.4328,1.6100;.1978,2.0703|9:.4961,.6192;.4568,1.2145;.3592,1.7429;.1489,2.1003;.5065,0|10:.4535,.2794;.4352,.8289;.3886,1.3448;.2908,1.7837;.1136,2.0599")
        };

    public static IReadOnlyList<int> GetOrders(FilterApproximation approximation) =>
        Tables.TryGetValue(approximation, out var table) ? table.Keys.Where(order => order >= MinimumPublishedOrder(approximation)).Order().ToArray() : Array.Empty<int>();

    public static IReadOnlyList<Complex> GetPoles(FilterApproximation approximation, int order)
    {
        if (order < MinimumPublishedOrder(approximation) || !Tables.TryGetValue(approximation, out var table) || !table.TryGetValue(order, out var poles))
            throw new NotSupportedException($"{approximation} has no published prototype for order {order}.");
        return poles;
    }

    private static int MinimumPublishedOrder(FilterApproximation approximation) => approximation is FilterApproximation.GaussianTo6Db or FilterApproximation.GaussianTo12Db ? 3 : 2;

    private static IReadOnlyDictionary<int, Complex[]> Parse(string source)
    {
        var result = new Dictionary<int, Complex[]>();
        foreach (var orderText in source.Split('|'))
        {
            var parts = orderText.Split(':'); var poles = new List<Complex>();
            foreach (var pairText in parts[1].Split(';'))
            {
                var pair = pairText.Split(','); var real = double.Parse(pair[0], CultureInfo.InvariantCulture); var imaginary = double.Parse(pair[1], CultureInfo.InvariantCulture);
                poles.Add(new Complex(-real, imaginary)); if (imaginary != 0) poles.Add(new Complex(-real, -imaginary));
            }
            result[int.Parse(parts[0], CultureInfo.InvariantCulture)] = poles.ToArray();
        }
        return result;
    }
}
