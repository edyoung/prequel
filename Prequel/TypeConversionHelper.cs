﻿namespace Prequel
{
    using System;
    using System.Collections.Generic;
    using Microsoft.SqlServer.TransactSql.ScriptDom;

    [Flags]
    public enum TypeConversionResult
    {
        /// <summary>
        /// SQL will convert from A to B implicitly and safely
        /// </summary>
        ImplicitSafe = 0,

        /// <summary>
        /// SQL will convert from A to B implicitly but data could be mangled in the process
        /// </summary>
        ImplicitLossy = 1,

        /// <summary>
        /// Only Explicit conversons allowed
        /// </summary>
        Explicit = 2,

        /// <summary>
        /// Will be implicitly converted but could be lossy depending on the size of the data (eg varchar)
        /// </summary>
        CheckLength = 4,

        /// <summary>
        /// String type will undergo unicode -> code page conversion
        /// </summary>
        Narrowing = 8,

        /// <summary>
        /// A cannot be converted to B
        /// </summary>
        NotAllowed = 16,

        /// <summary>
        /// A will be converted to string, check max length of that
        /// </summary>
        CheckConvertedLength = 32,

        /// <summary>
        /// A will be converted to a smaller numeric type, could lead to overflow
        /// </summary>
        NumericOverflow = 64,

        /// <summary>
        /// Prequel doesn't know what will happen. Claim everything is great
        /// </summary>
        NotImplemented = 1 << 16
    }

    public struct NumericTraits
    {
        public long Min { get; private set; }

        public long Max { get; private set; }

        /// <summary>
        /// if object A has a SizeClass >= object B's SizeClass, B can be assigned to A without risk of overlow
        /// </summary>
        public int SizeClass { get; private set;  } 

        public NumericTraits(long min, long max, int sizeClass)
        {
            Min = min;
            Max = max;
            SizeClass = sizeClass;
        }
    }

    public static class TypeConversionHelper
    {
        // Encodes the table in https://msdn.microsoft.com/en-us/library/ms191530.aspx showing which types can be converted 
        private static IDictionary<Tuple<SqlDataTypeOption, SqlDataTypeOption>, TypeConversionResult> conversionTable = CreateConversionTable();

        private static IDictionary<Tuple<SqlDataTypeOption, SqlDataTypeOption>, TypeConversionResult> CreateConversionTable()
        {
            var conversions = new Dictionary<Tuple<SqlDataTypeOption, SqlDataTypeOption>, TypeConversionResult>();

            // From Char
            conversions.Add(SqlDataTypeOption.Char, SqlDataTypeOption.Char, TypeConversionResult.CheckLength);
            conversions.Add(SqlDataTypeOption.Char, SqlDataTypeOption.VarChar, TypeConversionResult.CheckLength);
            conversions.Add(SqlDataTypeOption.Char, SqlDataTypeOption.NChar, TypeConversionResult.CheckLength);
            conversions.Add(SqlDataTypeOption.Char, SqlDataTypeOption.NVarChar, TypeConversionResult.CheckLength);

            // From NChar
            conversions.Add(SqlDataTypeOption.NChar, SqlDataTypeOption.Char, TypeConversionResult.CheckLength | TypeConversionResult.Narrowing);
            conversions.Add(SqlDataTypeOption.NChar, SqlDataTypeOption.VarChar, TypeConversionResult.CheckLength | TypeConversionResult.Narrowing);
            conversions.Add(SqlDataTypeOption.NChar, SqlDataTypeOption.NChar, TypeConversionResult.CheckLength);
            conversions.Add(SqlDataTypeOption.NChar, SqlDataTypeOption.NVarChar, TypeConversionResult.CheckLength);

            conversions.Add(SqlDataTypeOption.NChar, SqlDataTypeOption.Int, TypeConversionResult.ImplicitLossy);

            // From VarChar
            conversions.Add(SqlDataTypeOption.VarChar, SqlDataTypeOption.VarChar, TypeConversionResult.CheckLength);
            conversions.Add(SqlDataTypeOption.VarChar, SqlDataTypeOption.Char, TypeConversionResult.CheckLength);
            conversions.Add(SqlDataTypeOption.VarChar, SqlDataTypeOption.NChar, TypeConversionResult.CheckLength);
            conversions.Add(SqlDataTypeOption.VarChar, SqlDataTypeOption.NVarChar, TypeConversionResult.CheckLength);

            // from NVarChar
            conversions.Add(SqlDataTypeOption.NVarChar, SqlDataTypeOption.Char, TypeConversionResult.CheckLength | TypeConversionResult.Narrowing);
            conversions.Add(SqlDataTypeOption.NVarChar, SqlDataTypeOption.VarChar, TypeConversionResult.CheckLength | TypeConversionResult.Narrowing);
            conversions.Add(SqlDataTypeOption.NVarChar, SqlDataTypeOption.NChar, TypeConversionResult.CheckLength);
            conversions.Add(SqlDataTypeOption.NVarChar, SqlDataTypeOption.NVarChar, TypeConversionResult.CheckLength);

            // from Int
            conversions.Add(SqlDataTypeOption.Int, SqlDataTypeOption.Char, TypeConversionResult.CheckConvertedLength);
            conversions.Add(SqlDataTypeOption.Int, SqlDataTypeOption.VarChar, TypeConversionResult.CheckConvertedLength);
            conversions.Add(SqlDataTypeOption.Int, SqlDataTypeOption.NChar, TypeConversionResult.CheckConvertedLength);
            conversions.Add(SqlDataTypeOption.Int, SqlDataTypeOption.NVarChar, TypeConversionResult.CheckConvertedLength);

            conversions.Add(SqlDataTypeOption.Int, SqlDataTypeOption.TinyInt, TypeConversionResult.NumericOverflow);
            conversions.Add(SqlDataTypeOption.Int, SqlDataTypeOption.SmallInt, TypeConversionResult.NumericOverflow);            

            // from smallint
            conversions.Add(SqlDataTypeOption.SmallInt, SqlDataTypeOption.Char, TypeConversionResult.CheckConvertedLength);
            conversions.Add(SqlDataTypeOption.SmallInt, SqlDataTypeOption.VarChar, TypeConversionResult.CheckConvertedLength);
            conversions.Add(SqlDataTypeOption.SmallInt, SqlDataTypeOption.NChar, TypeConversionResult.CheckConvertedLength);
            conversions.Add(SqlDataTypeOption.SmallInt, SqlDataTypeOption.NVarChar, TypeConversionResult.CheckConvertedLength);

            // from bigint
            conversions.Add(SqlDataTypeOption.BigInt, SqlDataTypeOption.Char, TypeConversionResult.CheckConvertedLength);
            conversions.Add(SqlDataTypeOption.BigInt, SqlDataTypeOption.VarChar, TypeConversionResult.CheckConvertedLength);
            conversions.Add(SqlDataTypeOption.BigInt, SqlDataTypeOption.NChar, TypeConversionResult.CheckConvertedLength);
            conversions.Add(SqlDataTypeOption.BigInt, SqlDataTypeOption.NVarChar, TypeConversionResult.CheckConvertedLength);

            return conversions;
        }

        private static IDictionary<SqlDataTypeOption, int> precedenceTable = CreatePrecedenceTable();

        // Encodes the precedence rules in https://msdn.microsoft.com/en-us/library/ms190309.aspx
        private static IDictionary<SqlDataTypeOption, int> CreatePrecedenceTable()
        {            
            var table = new Dictionary<SqlDataTypeOption, int>();
            
            table[SqlDataTypeOption.None] = 0;
            
            // 1 == user defined
            table[SqlDataTypeOption.Sql_Variant] = 2;
            
            // 3 what happened to XML?
            table[SqlDataTypeOption.DateTimeOffset] = 4;
            table[SqlDataTypeOption.DateTime2] = 5;
            table[SqlDataTypeOption.DateTime] = 6;            
            table[SqlDataTypeOption.SmallDateTime] = 7;
            table[SqlDataTypeOption.Date] = 8;
            table[SqlDataTypeOption.Time] = 9;
            table[SqlDataTypeOption.Float] = 10;
            table[SqlDataTypeOption.Real] = 11;
            table[SqlDataTypeOption.Decimal] = 12;
            table[SqlDataTypeOption.Money] = 13;
            table[SqlDataTypeOption.SmallMoney] = 14;
            table[SqlDataTypeOption.BigInt] = 15;
            table[SqlDataTypeOption.Int] = 16;
            table[SqlDataTypeOption.SmallInt] = 17;
            table[SqlDataTypeOption.TinyInt] = 18;
            table[SqlDataTypeOption.Bit] = 19;
            table[SqlDataTypeOption.NText] = 20;
            table[SqlDataTypeOption.Text] = 21;
            table[SqlDataTypeOption.Image] = 22;
            table[SqlDataTypeOption.Timestamp] = 23;
            table[SqlDataTypeOption.UniqueIdentifier] = 24;
            table[SqlDataTypeOption.NVarChar] = 25;
            table[SqlDataTypeOption.NChar] = 26;
            table[SqlDataTypeOption.VarChar] = 27;
            table[SqlDataTypeOption.Char] = 28;
            table[SqlDataTypeOption.VarBinary] = 29;
            table[SqlDataTypeOption.Binary] = 30;
            return table;
        }

        private static IDictionary<SqlDataTypeOption, NumericTraits> numericLimitTable = CreateNumericLimitTable();

        // Encodes the size info from https://msdn.microsoft.com/en-us/library/ms187745.aspx
        private static IDictionary<SqlDataTypeOption, NumericTraits> CreateNumericLimitTable()
        {
            var table = new Dictionary<SqlDataTypeOption, NumericTraits>();

            table.Add(SqlDataTypeOption.Int, new NumericTraits(Int32.MinValue, Int32.MaxValue, sizeClass: 4));
            table.Add(SqlDataTypeOption.SmallInt, new NumericTraits(Int16.MinValue, Int16.MinValue, sizeClass: 2));
            return table;
        }

        internal static bool IsHigherPrecedence(SqlDataTypeOption t1, SqlDataTypeOption t2)
        {
            int precedenceOfType1 = 0;
            precedenceTable.TryGetValue(t1, out precedenceOfType1);
            int precedenceOfType2 = 0;
            precedenceTable.TryGetValue(t2, out precedenceOfType2);
            return precedenceOfType1 < precedenceOfType2;            
        }

        public static TypeConversionResult GetConversionResult(SqlDataTypeOption from, SqlDataTypeOption to)
        {
            TypeConversionResult result = TypeConversionResult.NotImplemented;
            conversionTable.TryGetValue(new Tuple<SqlDataTypeOption, SqlDataTypeOption>(from, to), out result);
            return result;
        }

        public static void Add(this IDictionary<Tuple<SqlDataTypeOption, SqlDataTypeOption>, TypeConversionResult> dictionary, SqlDataTypeOption from, SqlDataTypeOption to, TypeConversionResult result)
        {
            dictionary.Add(new Tuple<SqlDataTypeOption, SqlDataTypeOption>(from, to), result);
        }
    }
}