using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;
using System.Threading;

public static class Utility
{
    public static string WrapTextColor(string text, Color32 c)
    {
        return $"<color={HexCOLOR(c)}>{text}</color>";
    }
    public static string HexCOLOR(Color32 c)
    {
        return $"#{c.r:X2}{c.g:X2}{c.b:X2}{c.a:X2}"; ;
    }
    public static string GetEnumString(System.Type type, object value)
    {
        return System.Enum.GetName(type, value);
    }

    public static string GetRelativeDayString(DateTime current, DateTime target)
    {
        var difference = current - target;
        if (difference.Days >= 365)
        {
            return $"{current.Year - target.Year} years ago";
        }
        else if (difference.Days >= 30)
        {
            return $"{(current.Month < target.Month ? current.Month + 12 : current.Month) - target.Month} month ago";
        }
        else if (current.DayOfYear != target.DayOfYear)
        {
            return $"{Math.Ceiling(difference.TotalDays)} days ago";
        }
        else return "today"; 
    }

    /// <summary>
    /// Provides a thread-safe, thread-static System.Random instance for each thread.
    /// Lazily initializes a new instance with a thread-specific seed if null.
    /// </summary>
    public static System.Random Random
    {
        get
        {
            if (_random == null)
                _random = new System.Random(Environment.TickCount ^ Thread.CurrentThread.ManagedThreadId);
            return _random;
        }
    }

    // Thread-static backing field for Random
    [ThreadStatic]
    private static System.Random _random;

    public static void Shuffle<T>(List<T> list, System.Random rand = null)
    {
        var random = rand == null ? Random : rand;
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = random.Next(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    /// <summary>
    /// Only accept Dictionary with object key and int:weight value
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="dict"></param>
    /// <param name="rand"></param>
    /// <returns></returns>
    public static T WeightedRandInDict<T>(Dictionary<T, int> dict, System.Random rand = null)
    {
        var random = rand == null ? Random : rand;
        var total = dict.Values.Sum();
        var randW = random.Next(0, total);

        var current = default(T);
        foreach(var kvp in dict)
        {
            current = kvp.Key;
            if (randW <= kvp.Value) return kvp.Key;
            else randW -= kvp.Value;
        }
        return current;

    }
    public static bool CompareValue(float value1, LogicalOperand operand, float value2)
    {

        switch (operand)
        {
            case LogicalOperand.eq:
                return value1 == value2;
            case LogicalOperand.neq:
                return value1 != value2;
            case LogicalOperand.gte:
                return value1 >= value2;
            case LogicalOperand.lte:
                return value1 <= value2;
            case LogicalOperand.gt:
                return value1 > value2;
            case LogicalOperand.lt:
                return value1 < value2;
            default:
                Debug.LogError("CompareValue (int) Error: invalid operand");
                return false;
        }
    }

    public static bool CompareValue(int value1, LogicalOperand operand, int value2)
    {

        switch (operand)
        {
            case LogicalOperand.eq:
                return value1 == value2;
            case LogicalOperand.neq:
                return value1 != value2;
            case LogicalOperand.gte:
                return value1 >= value2;
            case LogicalOperand.lte:
                return value1 <= value2;
            case LogicalOperand.gt:
                return value1 > value2;
            case LogicalOperand.lt:
                return value1 < value2;
            default:
                Debug.LogError("CompareValue (int) Error: invalid operand");
                return false;
        }
    }
    public static bool CompareValue(bool value1, LogicalOperand operand, string value2)
    {
        //Debug.LogError("Comparevalue climaxed ["+value1+"] ["+operand+"] ["+value2+"]");
        bool value;
        if (!bool.TryParse(value2, out value))
        {
            Debug.LogError("CompareValue (bool) Error: cannot parse value into boolean");
            return false;
        }

        // modify invalid operands into valid ones
        if (operand == LogicalOperand.gte || operand == LogicalOperand.lte) operand = LogicalOperand.eq;
        else if (operand == LogicalOperand.gt || operand == LogicalOperand.lt) operand = LogicalOperand.neq;

        switch (operand)
        {
            case LogicalOperand.eq:
                return value1 == value;
            case LogicalOperand.neq:
                return value1 != value;
            default:
                Debug.LogError("CompareValue (boolean) Error: invalid operand");
                return false;
        }
    }
    public static bool CompareValue(bool value1, LogicalOperand operand, bool value)
    {
        //Debug.LogError("Comparevalue climaxed ["+value1+"] ["+operand+"] ["+value2+"]");
        // modify invalid operands into valid ones
        //if (operand == LogicalOperand.gte || operand == LogicalOperand.lte) operand = LogicalOperand.eq;
        //else if (operand == LogicalOperand.gt || operand == LogicalOperand.lt) operand = LogicalOperand.neq;

        switch (operand)
        {
            case LogicalOperand.eq:
            case LogicalOperand.gte:
            case LogicalOperand.lte:
                return value1 == value;
            case LogicalOperand.neq:
            case LogicalOperand.gt:
            case LogicalOperand.lt:
                return value1 != value;
            default:
                Debug.LogError("CompareValue (boolean) Error: invalid operand");
                return false;
        }
    }

    public static T GetMaxWeightInDict<T>(Dictionary<T, int> dict)
    {
        T maxKey = default(T);
        int maxValue = int.MinValue;

        foreach (var kvp in dict)
        {
            if (kvp.Value > maxValue)
            {
                maxValue = kvp.Value;
                maxKey = kvp.Key;
            }
        }

        return maxKey;
    }

    /// <summary>
    /// return the loop count it takes to get desired result with dice
    /// </summary>
    /// <param name="diceMax">dice will be thrown in range 0 - diceMax</param>
    /// <param name="maxIteration">max loop count allowed, this is also the return value if all fails</param>
    /// <param name="successThreshold">return current iteration when dice <= threshold</param>
    /// <param name="rand"></param>
    /// <returns></returns>
    public static int DiceUntil(int diceMax, int maxIteration, int successThreshold, System.Random rand = null)
    {
        var random = rand == null ? Random : rand;
        for (int i = 0; i < maxIteration; i++)
        {
            var randW = random.Next(0, diceMax);
            if (randW <= successThreshold) return i;
        }
        return maxIteration;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="chance">range from 0.0 to 1.0</param>
    /// <param name="rand">randomizer object</param>
    /// <returns></returns>
    public static bool RandomChance(double chance, System.Random rand = null)
    {
        var random = rand == null ? Random : rand;
        return random.NextDouble() < chance;
    }
    public static double RandVariation(double baseNumber, double maxMult, System.Random rand = null)
    {
        var random = rand == null ? Random : rand;
        double min = baseNumber / maxMult;
        double max = baseNumber * maxMult;
        return min + random.NextDouble() * (max - min);
    }

    public static float RandVariation(float baseNumber, float maxVariation, System.Random rand = null)
    {
        var random = rand == null ? Random : rand;
        float min = baseNumber - maxVariation;
        float max = baseNumber + maxVariation;
        return min + (float)(random.NextDouble() * (max - min));
    }

    /// <summary>
    /// Return [count, count*face]
    /// </summary>
    /// <param name="count"></param>
    /// <param name="face"></param>
    /// <param name="rand"></param>
    /// <returns></returns>
    public static int Dice(int count, int face, System.Random rand = null)
    {
        var random = rand == null ? Random : rand;
        int total = 0;
        for (int i = 0; i < count; i++)
        {
            total += random.Next(1, face + 1); // inclusive min, exclusive max
        }
        return total;
    }
    public static List<T> Distinct<T>(List<T> input, IEqualityComparer<T> comparer = null)
    {
        if (input == null)
            return new List<T>();

        comparer = comparer ?? EqualityComparer<T>.Default;
        HashSet<T> seen = new(comparer);
        List<T> result = new(Math.Min(input.Count, 16)); // Conservative pre-allocation

        foreach (var item in input)
        {
            if (seen.Add(item))
                result.Add(item);
        }

        return result;
    }

    public static T GetRandomElement<T>(List<T> list)
    {
        if (list == null || list.Count == 0)
            return default;

        int index = Random.Next(0, list.Count); // range: [0, Count - 1]
        return list[index];
    }

    public static int GetRandIndexFromListCount<T>(List<T> list)
    {
        if (list == null) return 0;
        return Random.Next(0, list.Count);
    }

    public static void ShuffleList<T>(List<T> list)
    {
        FilterRandXInList(list, list.Count);
    }

    public static void FilterRandXInList<T>(List<T> list, int count, System.Random rand = null)
    {
        if (list == null) return;
        if (count < 0) return;
        var random = rand == null ? Random : rand;
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = random.Next(0, i + 1);
            // Swap elements at i and j
            T temp = list[i];
            list[i] = list[j];
            list[j] = temp;
        }
        // Remove excess elements from the end until list.Count <= count
        while (list.Count > count)
        {
            list.RemoveAt(list.Count - 1);
        }
    }

    /// <summary>
    /// Check if L2 contain at least 1 element of L1. Also return true if L2 is empty
    /// </summary>
    /// <returns></returns>
    public static bool ListContainsLoose<T>(List<T> L1, List<T> L2, IEqualityComparer<T> comparer = null)
    {
        if (L2 == null || L2.Count == 0) return true;
        if (L1 == null) return false;

        foreach (var item in L2)
        {
            if (L1.Contains(item)) return true;
        }
        return false;
        //return !distinctL2.Except(distinctL1, comparer).Any();
        //return distinctL2.Any(item => distinctL1.Contains(item, comparer));
    }

    public static string LogicOperandToString(LogicalOperand op)
    {
        switch (op)
        {
            case LogicalOperand.gte: return ">=";
            case LogicalOperand.lte: return "<=";
            case LogicalOperand.eq: return "==";
            case LogicalOperand.neq: return "!=";
            case LogicalOperand.gt: return ">";
            case LogicalOperand.lt: return "<";
            default: return "??";
        }
    }

    /// <summary>
    /// Check if L2 is contained in L1. Also return true if L2 is empty
    /// </summary>
    /// <returns></returns>
    public static bool ListContainsStrict<T>(List<T> L1, List<T> L2, IEqualityComparer<T> comparer = null)
    {
        if (L2 == null || L2.Count == 0) return true;
        if (L1 == null) return false;

        foreach (var item in L2)
        {
            if (!L1.Contains(item))
                return false;
        }
        return true;
    }
    // Stores the mapping from the human-readable string key to the unique integer ID.
    private static readonly Dictionary<string, int> _idLookup = new Dictionary<string, int>();
    private static readonly Dictionary<int, string > _stringLookup = new Dictionary<int, string>();

    // The counter used to assign the next available unique integer ID.
    private static int _nextID = 1; // Start at 1 to avoid conflicts with 0 (default/null value)

    // Used to ensure thread safety when allocating a new ID.
    private static readonly object _lock = new object();

    /// <summary>
    /// Retrieves the unique integer ID for a given string key. 
    /// If the key does not exist, a new unique ID is allocated and cached. <br/><br/>
    /// NOTE: THIS FUNCTION DOES NOT STORE ID
    /// </summary>
    /// <param name="key">The string identifier (e.g., "AttackPower", "Speed").</param>
    /// <returns>The guaranteed unique integer ID for that key.</returns>
    public static int GetUniqueID(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            Debug.LogError("Attempted to get ID for null or empty key.");
            return 0; // Return a safe default/invalid ID
        }

        // 1. Attempt non-locking read first for maximum read-speed
        if (_idLookup.TryGetValue(key, out int existingID))
        {
            return existingID;
        }

        // 2. If the key is new, we must allocate a new ID safely
        lock (_lock)
        {
            // Re-check after acquiring the lock in case another thread allocated it
            // while this thread was waiting for the lock.
            if (_idLookup.TryGetValue(key, out existingID))
            {
                return existingID;
            }

            // Allocate new ID
            int newID = _nextID;
            _idLookup.Add(key, newID);
            _stringLookup.Add(newID, key);
            _nextID++;

            return newID;
        }
    }
    public static string GetStringByUniqueID(int UniqueID)
    {
        // 1. Attempt non-locking read first for maximum read-speed
        if (_stringLookup.TryGetValue(UniqueID, out string existingID))
        {
            return existingID;
        }
        return "";
    }



    /// <summary>
    /// Contains List.Distinct() which may interfere with equality comparison ?
    /// </summary>
    /// <param name="L1"></param>
    /// <param name="L2"></param>
    /// <returns></returns>
    public static bool ListEquals<T>(List<T> L1, List<T> L2, IEqualityComparer<T> comparer = null)
    {
        if (L1 == null || L1.Count == 0)
            return L2 == null || L2.Count == 0;
        if (L2 == null || L2.Count == 0)
            return false;

        comparer = comparer ?? EqualityComparer<T>.Default;

        HashSet<T> setL1 = new(L1, comparer);
        HashSet<T> setL2 = new(L2, comparer);

        return setL1.SetEquals(setL2);
    }

    public static void DestroyAllChildrenFrom(RectTransform rect, int startFromIndex = 0, bool useDestroyImmediate = false)
    {
        if (rect == null)
            return;

        if (startFromIndex < 0)
            startFromIndex = 0;

        for (int i = rect.transform.childCount - 1; i >= startFromIndex; i--)
        {
            var child = rect.transform.GetChild(i);
            if (useDestroyImmediate)
                UnityEngine.Object.DestroyImmediate(child.gameObject);
            else
                UnityEngine.Object.Destroy(child.gameObject);
        }
    }
}

/// <summary>
/// Grok<br/>
/// Custom Types: If T is a Unity type (e.g., GameObject), ensure the comparer handles object lifetime (e.g., checks for null or destroyed objects). Example:
/// </summary>
public class GameObjectComparer : IEqualityComparer<GameObject>
{
    public bool Equals(GameObject x, GameObject y)
    {
        if (ReferenceEquals(x, null) || ReferenceEquals(y, null))
            return false;
        if (ReferenceEquals(x, y))
            return true;
        return x.name == y.name;
    }

    public int GetHashCode(GameObject obj)
    {
        return obj != null ? obj.name.GetHashCode() : 0;
    }
}