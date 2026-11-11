// The pinned official source uses recent BCL argument-guard conveniences. This namespace-local adapter supplies
// those guards for netstandard2.1; the official files retain their original bytes and the solver is unchanged.
namespace RVO
{
    internal static class ArgumentOutOfRangeException
    {
        internal static void ThrowIfNegative(int value, string name)
        {
            if (value < 0)
            {
                throw new System.ArgumentOutOfRangeException(name, value, "Value must not be negative.");
            }
        }

        internal static void ThrowIfNegative(float value, string name)
        {
            if (value < 0)
            {
                throw new System.ArgumentOutOfRangeException(name, value, "Value must not be negative.");
            }
        }

        internal static void ThrowIfNegativeOrZero(float value, string name)
        {
            if (value <= 0)
            {
                throw new System.ArgumentOutOfRangeException(name, value, "Value must be positive.");
            }
        }

        internal static void ThrowIfGreaterThanOrEqual(int value, int other, string name)
        {
            if (value >= other)
            {
                throw new System.ArgumentOutOfRangeException(name, value, "Value must be smaller than the upper bound.");
            }
        }
    }
}
