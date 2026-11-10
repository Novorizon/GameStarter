/*
 * Vector2.cs
 * RVO2 Library C#
 *
 * SPDX-FileCopyrightText: 2008 University of North Carolina at Chapel Hill
 * SPDX-License-Identifier: Apache-2.0
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 *
 * Please send all bug reports to <geom@cs.unc.edu>.
 *
 * The authors may be contacted via:
 *
 * Jur van den Berg, Stephen J. Guy, Jamie Snape, Ming C. Lin, Dinesh Manocha
 * Dept. of Computer Science
 * 201 S. Columbia St.
 * Frederick P. Brooks, Jr. Computer Science Bldg.
 * Chapel Hill, N.C. 27599-3175
 * United States of America
 *
 * <http://gamma.cs.unc.edu/RVO2/>
 */

using System;
using System.Globalization;

namespace RVO
{
    /// <summary>Defines a two-dimensional vector.</summary>
    public readonly struct Vector2 : IEquatable<Vector2>
    {
        internal readonly float _x;
        internal readonly float _y;

        /// <summary>Constructs and initializes a two-dimensional vector from the
        /// specified xy-coordinates.</summary>
        ///
        /// <param name="x">The x-coordinate of the two-dimensional vector.
        /// </param>
        /// <param name="y">The y-coordinate of the two-dimensional vector.
        /// </param>
        public Vector2(float x, float y)
        {
            _x = x;
            _y = y;
        }

        /// <summary>Returns the string representation of this vector.</summary>
        ///
        /// <returns>The string representation of this vector.</returns>
        public override string ToString()
        {
            return $"({_x.ToString(CultureInfo.InvariantCulture)},{_y.ToString(CultureInfo.InvariantCulture)})";
        }

        /// <summary>Returns true if this vector equals the specified vector.
        /// </summary>
        ///
        /// <returns>True if this vector equals the specified vector.</returns>
        ///
        /// <param name="other">The vector to compare with this vector.</param>
        public bool Equals(Vector2 other)
        {
            return _x == other._x && _y == other._y;
        }

        /// <summary>Returns true if this vector equals the specified object.
        /// </summary>
        ///
        /// <returns>True if this vector equals the specified object.</returns>
        ///
        /// <param name="obj">The object to compare with this vector.</param>
        public override bool Equals(object obj)
        {
            return obj is Vector2 other && Equals(other);
        }

        /// <summary>Returns the hash code for this vector.</summary>
        ///
        /// <returns>The hash code for this vector.</returns>
        public override int GetHashCode()
        {
            return HashCode.Combine(_x, _y);
        }

        /// <summary>Returns true if the two vectors are equal.</summary>
        ///
        /// <returns>True if the two vectors are equal.</returns>
        ///
        /// <param name="left">The first vector.</param>
        /// <param name="right">The second vector.</param>
        public static bool operator ==(Vector2 left, Vector2 right)
        {
            return left.Equals(right);
        }

        /// <summary>Returns true if the two vectors are not equal.</summary>
        ///
        /// <returns>True if the two vectors are not equal.</returns>
        ///
        /// <param name="left">The first vector.</param>
        /// <param name="right">The second vector.</param>
        public static bool operator !=(Vector2 left, Vector2 right)
        {
            return !left.Equals(right);
        }

        /// <summary>Gets the x-coordinate of this two-dimensional vector.
        /// </summary>
        ///
        /// <value>The x-coordinate of the two-dimensional vector.</value>
        public float X => _x;

        /// <summary>Gets the y-coordinate of this two-dimensional vector.
        /// </summary>
        ///
        /// <value>The y-coordinate of the two-dimensional vector.</value>
        public float Y => _y;

        /// <summary>Returns the x-coordinate of this two-dimensional vector.
        /// </summary>
        ///
        /// <returns>The x-coordinate of the two-dimensional vector.</returns>
        [Obsolete("Use the X property instead.", false)]
        public float x()
        {
            return _x;
        }

        /// <summary>Returns the y-coordinate of this two-dimensional vector.
        /// </summary>
        ///
        /// <returns>The y-coordinate of the two-dimensional vector.</returns>
        [Obsolete("Use the Y property instead.", false)]
        public float y()
        {
            return _y;
        }

        /// <summary>Computes the dot product of the two specified
        /// two-dimensional vectors.</summary>
        ///
        /// <returns>The dot product of the two specified two-dimensional
        /// vectors.</returns>
        ///
        /// <param name="vector1">The first two-dimensional vector.</param>
        /// <param name="vector2">The second two-dimensional vector.</param>
        public static float operator *(Vector2 vector1, Vector2 vector2)
        {
            return vector1._x * vector2._x + vector1._y * vector2._y;
        }

        /// <summary>Computes the scalar multiplication of the specified
        /// two-dimensional vector with the specified scalar value.</summary>
        ///
        /// <returns>The scalar multiplication of the specified two-dimensional
        /// vector with the specified scalar value.</returns>
        ///
        /// <param name="scalar">The scalar value.</param>
        /// <param name="vector">The two-dimensional vector.</param>
        public static Vector2 operator *(float scalar, Vector2 vector)
        {
            return vector * scalar;
        }

        /// <summary>Computes the scalar multiplication of the specified
        /// two-dimensional vector with the specified scalar value.</summary>
        ///
        /// <returns>The scalar multiplication of the specified two-dimensional
        /// vector with the specified scalar value.</returns>
        ///
        /// <param name="vector">The two-dimensional vector.</param>
        /// <param name="scalar">The scalar value.</param>
        public static Vector2 operator *(Vector2 vector, float scalar)
        {
            return new Vector2(vector._x * scalar, vector._y * scalar);
        }

        /// <summary>Computes the scalar division of the specified
        /// two-dimensional vector with the specified scalar value.</summary>
        ///
        /// <returns>The scalar division of the specified two-dimensional vector
        /// with the specified scalar value.</returns>
        ///
        /// <param name="vector">The two-dimensional vector.</param>
        /// <param name="scalar">The scalar value.</param>
        public static Vector2 operator /(Vector2 vector, float scalar)
        {
            return new Vector2(vector._x / scalar, vector._y / scalar);
        }

        /// <summary>Computes the vector sum of the two specified two-dimensional
        /// vectors.</summary>
        ///
        /// <returns>The vector sum of the two specified two-dimensional vectors.
        /// </returns>
        ///
        /// <param name="vector1">The first two-dimensional vector.</param>
        /// <param name="vector2">The second two-dimensional vector.</param>
        public static Vector2 operator +(Vector2 vector1, Vector2 vector2)
        {
            return new Vector2(vector1._x + vector2._x, vector1._y + vector2._y);
        }

        /// <summary>Computes the vector difference of the two specified
        /// two-dimensional vectors</summary>
        ///
        /// <returns>The vector difference of the two specified two-dimensional
        /// vectors.</returns>
        ///
        /// <param name="vector1">The first two-dimensional vector.</param>
        /// <param name="vector2">The second two-dimensional vector.</param>
        public static Vector2 operator -(Vector2 vector1, Vector2 vector2)
        {
            return new Vector2(vector1._x - vector2._x, vector1._y - vector2._y);
        }

        /// <summary>Computes the negation of the specified two-dimensional
        /// vector.</summary>
        ///
        /// <returns>The negation of the specified two-dimensional vector.
        /// </returns>
        ///
        /// <param name="vector">The two-dimensional vector.</param>
        public static Vector2 operator -(Vector2 vector)
        {
            return new Vector2(-vector._x, -vector._y);
        }
    }
}
