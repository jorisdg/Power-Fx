﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.PowerFx.Core;
using Microsoft.PowerFx.Core.Binding;
using Microsoft.PowerFx.Core.Functions;
using Microsoft.PowerFx.Core.Glue;
using Microsoft.PowerFx.Core.Types;
using Microsoft.PowerFx.Core.Types.Enums;
using Microsoft.PowerFx.Core.Utils;
using Microsoft.PowerFx.Intellisense;
using Microsoft.PowerFx.Syntax;
using Microsoft.PowerFx.Types;

namespace Microsoft.PowerFx
{
    /// <summary>
    /// Expose binding logic for Power Fx. 
    /// Derive from this to provide evaluation abilities. 
    /// </summary>
    public class Engine : IPowerFxEngine
    {
        /// <summary>
        /// Configuration symbols for this Power Fx engine.
        /// </summary>
        public PowerFxConfig Config { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="Engine"/> class.
        /// </summary>
        /// <param name="powerFxConfig"></param>
        public Engine(PowerFxConfig powerFxConfig)
        {
            Config = powerFxConfig;
        }

        // All functions that powerfx core knows about. 
        // Derived engines may only support a subset of these builtins, 
        // and they may add their own custom ones. 
        private static readonly ReadOnlySymbolTable _allBuiltinCoreFunctions = ReadOnlySymbolTable.NewDefault(Core.Texl.BuiltinFunctionsCore.BuiltinFunctionsLibrary);

        /// <summary>
        /// Builtin functions supported by this engine. 
        /// </summary>
        public ReadOnlySymbolTable SupportedFunctions { get; protected internal set; } = _allBuiltinCoreFunctions;

        // By default, we pull the core functions. 
        // These can be overridden. 
        internal IEnumerable<TexlFunction> Functions => CreateResolverInternal().Functions;

        /// <summary>
        /// Get all functions from the config and symbol tables. 
        /// </summary>
        public IEnumerable<FunctionInfo> FunctionInfos => Functions.Select(f => new FunctionInfo(f));

        /// <summary>
        /// List all functions (both builtin and custom) registered with this evaluator. 
        /// </summary>
        public IEnumerable<string> GetAllFunctionNames()
        {
            return FunctionInfos.Select(func => func.Name).Distinct();
        }

        // Additional symbols for the engine.
        // A derived engine can replace this completely to inject engine-specific virtuals. 
        // These symbols then feed into the resolver
        protected ReadOnlySymbolTable EngineSymbols { get; set; }

        /// <summary>
        /// Create a resolver for use in binding. This is called from <see cref="Check(string, RecordType, ParserOptions)"/>.
        /// Base classes can override this is there are additional symbols not in the config.
        /// </summary>
        [Obsolete("Use EngineSymbols instead.")]
        private protected virtual INameResolver CreateResolver()
        {
            return null;
        }

        // Returns the INameResolver  and the corresponding Symbol table.         
        private protected INameResolver CreateResolverInternal(ReadOnlySymbolTable localSymbols = null)
        {
            return CreateResolverInternal(out _, localSymbols);
        }

        private protected INameResolver CreateResolverInternal(out ReadOnlySymbolTable symbols, ReadOnlySymbolTable localSymbols = null)
        {
            // For backwards compat with Prose.
#pragma warning disable CS0612 // Type or member is obsolete
#pragma warning disable CS0618 // Type or member is obsolete
            var existing = CreateResolver();
#pragma warning restore CS0618 // Type or member is obsolete
#pragma warning restore CS0612 // Type or member is obsolete
            if (existing != null)
            {
                symbols = null;
                return existing;
            }

            var list = new SymbolTableEnumerator(localSymbols, EngineSymbols, SupportedFunctions, Config.SymbolTable);
            symbols = new ComposedReadOnlySymbolTable(list);
            return symbols;
        }

        private protected virtual IBinderGlue CreateBinderGlue()
        {
            return new Glue2DocumentBinderGlue();
        }

        /// <summary>
        ///     Tokenize an expression to a sequence of <see cref="Token" />s.
        /// </summary>
        /// <param name="expressionText"></param>
        /// <returns></returns>
        public IReadOnlyList<Token> Tokenize(string expressionText)
            => TexlLexer.GetLocalizedInstance(Config.CultureInfo).GetTokens(expressionText);

        /// <summary>
        /// Parse the expression without doing any binding.
        /// </summary>
        /// <param name="expressionText"></param>
        /// <param name="options"></param>
        /// <returns></returns>
        public ParseResult Parse(string expressionText, ParserOptions options = null)
        {
            return Parse(expressionText, Config.Features, options, Config.CultureInfo);
        }

        /// <summary>
        /// Parse the expression without doing any binding.
        /// </summary>
        /// <param name="expressionText"></param>
        /// <param name="options"></param>
        /// <param name="cultureInfo"></param>
        /// <returns></returns>
        public static ParseResult Parse(string expressionText, ParserOptions options = null, CultureInfo cultureInfo = null)
        {
            return Parse(expressionText, Features.None, options, cultureInfo);
        }

        /// <summary>
        /// Parse the expression without doing any binding.
        /// </summary>
        public static ParseResult Parse(string expressionText, Features features, ParserOptions options = null, CultureInfo cultureInfo = null)
        {
            options ??= new ParserOptions();

            // If culture isn't explicitly set, use the one from PowerFx Config
            options.Culture ??= cultureInfo;

            var result = options.Parse(expressionText, features);
            return result;
        }

        /// <summary>
        /// Parse and Bind an expression. 
        /// </summary>
        /// <param name="expressionText">the expression in plain text. </param>
        /// <param name="parameterType">types of additional args to pass.</param>
        /// <param name="options">parser options to use.</param>
        /// <returns></returns>
        public CheckResult Check(string expressionText, RecordType parameterType, ParserOptions options = null)
        {
            var parse = Parse(expressionText, options);
            return Check(parse, parameterType, options);
        }

        /// <summary>
        /// Type check a formula without executing it. 
        /// </summary>
        /// <param name="parse">the parsed expression. Obtain from <see cref="Parse(string, ParserOptions)"/>.</param>
        /// <param name="parameterType">types of additional args to pass.</param>
        /// <param name="options">parser options to use.</param>
        /// <returns></returns>
        public CheckResult Check(ParseResult parse, RecordType parameterType, ParserOptions options = null)
        {
            var bindingConfig = new BindingConfig(allowsSideEffects: options?.AllowsSideEffects == true);
            var symbols = SymbolTable.NewFromRecord(parameterType);

            return CheckInternal(parse, bindingConfig, symbols);
        }

        public CheckResult Check(
            string expressionText,
            ParserOptions options = null,
            ReadOnlySymbolTable symbolTable = null)
        {
            var parse = Parse(expressionText, options);
            return Check(parse, options, symbolTable);
        }

        public CheckResult Check(
            ParseResult parse,
            ParserOptions options = null,
            ReadOnlySymbolTable symbolTable = null)
        {
            var bindingConfig = new BindingConfig(allowsSideEffects: options?.AllowsSideEffects == true);

            return CheckInternal(parse, bindingConfig, symbolTable);
        }

        private CheckResult CheckInternal(
            ParseResult parse,
            BindingConfig bindingConfig = null,
            ReadOnlySymbolTable symbolTable = null)
        {
            var startHash = symbolTable?.VersionHash;

            // Ok to continue with binding even if there are parse errors. 
            // We can still use that for intellisense.             
            var resolver = CreateResolverInternal(out var combinedSymbols, symbolTable);
            
            var glue = CreateBinderGlue();

            var binding = TexlBinding.Run(
                glue,
                parse.Root,
                resolver,
                bindingConfig,
                ruleScope: null,
                features: Config.Features);

            var result = new CheckResult(parse, Config.CultureInfo, binding)
            {
                Source = this,
                Symbols = combinedSymbols, // all symbols
                Parameters = symbolTable // just the parameters (provided at runtime)
            };

            if (result.IsSuccess && !result.HasDeferredArgsWarning)
            {
                result.TopLevelIdentifiers = DependencyFinder.FindDependencies(binding.Top, binding);

                // TODO: Fix FormulaType.Build to not throw exceptions for Enum types then remove this check
                if (binding.ResultType.Kind != DKind.Enum)
                {
                    result.ReturnType = FormulaType.Build(binding.ResultType);
                }

                result.Expression = CreateEvaluator(result);
            }

            var endHash = symbolTable?.VersionHash;
            if (startHash != endHash)
            {
                throw new InvalidOperationException("SymbolTable was mutated during binding of ${expressionText}");
            }

            return result;
        }

        /// <summary>
        /// Derived class can override to provide evaluation ability. Called after binding to return an eval object. 
        /// </summary>
        /// <param name="result">results of binding.</param>
        /// <returns>An <see cref="IExpression"/> to apply to the result.</returns>
        protected virtual IExpression CreateEvaluator(CheckResult result)
        {
            // Nop. 
            return null;
        }

        /// <summary>
        /// Optional hook to customize intellisense. 
        /// </summary>
        /// <returns></returns>
        private protected virtual IIntellisense CreateIntellisense()
        {
            return IntellisenseProvider.GetIntellisense(Config);
        }

        public IIntellisenseResult Suggest(string expression, RecordType parameterType, int cursorPosition)
        {
            var checkResult = Check(expression, parameterType);
            return Suggest(checkResult, cursorPosition);
        }

        /// <summary>
        /// Get intellisense from the formula, with parser options.
        /// </summary>
        public IIntellisenseResult Suggest(CheckResult checkResult, int cursorPosition)
        {
            var expression = checkResult.Parse.Text;

#pragma warning disable CS0618 // Type or member is obsolete
            return Suggest(expression, checkResult, cursorPosition);
#pragma warning restore CS0618 // Type or member is obsolete
        }

        /// <summary>
        /// Get intellisense from the formula, with parser options.
        /// </summary>
        [Obsolete("Use overload without expression")]
        public IIntellisenseResult Suggest(string expression, CheckResult checkResult, int cursorPosition)
        {            
            var binding = checkResult._binding;
            var formula = new Formula(expression, Config.CultureInfo);
            formula.ApplyParse(checkResult.Parse);

            // CheckResult has the binding, which has already captured both the INameResolver and any row scope parameters. 
            // So these both become available to intellisense. 
            var context = new IntellisenseContext(expression, cursorPosition);
            var intellisense = CreateIntellisense();
            var suggestions = intellisense.Suggest(context, binding, formula);

            return suggestions;
        }

        /// <summary>
        /// Creates a renamer instance for updating a field reference from <paramref name="parameters"/> in expressions.
        /// </summary>
        /// <param name="parameters">Type of parameters for formula. The fields in the parameter record can 
        /// be acecssed as top-level identifiers in the formula. Must be the names from before any rename operation is applied.</param>
        /// <param name="pathToRename">Path to the field to rename.</param>
        /// <param name="updatedName">New name. Replaces the last segment of <paramref name="pathToRename"/>.</param>
        /// <returns></returns>
        public RenameDriver CreateFieldRenamer(RecordType parameters, DPath pathToRename, DName updatedName)
        {
            Contracts.CheckValue(parameters, nameof(parameters));
            Contracts.CheckValid(pathToRename, nameof(pathToRename));
            Contracts.CheckValid(updatedName, nameof(updatedName));

            /* 
            ** PowerFxConfig handles symbol lookup in TryGetSymbol. As part of that, if that global entity 
            ** has a display name and we're in the process of converting an expression from invariant -> display,
            ** we also return that entities display name so it gets updated. 
            ** For Rename, we're reusing that invariant->display support, but only doing it for a single name,
            ** specified by `pathToRename`. So, we need to make sure that names in PowerFxConfig still bind, 
            ** but that we don't return any display names for them. Thus, we clone a PowerFxConfig but without 
            ** display name support and construct a resolver from that instead, which we use for the rewrite binding.
            */
            return new RenameDriver(parameters, pathToRename, updatedName, this, CreateResolverInternal(), CreateBinderGlue());
        }

        /// <summary>
        /// Convert references in an expression to the invariant form.
        /// </summary>
        /// <param name="expressionText">textual representation of the formula.</param>
        /// <param name="parameters">Type of parameters for formula. The fields in the parameter record can 
        /// be acecssed as top-level identifiers in the formula. If DisplayNames are used, make sure to have that mapping
        /// as part of the RecordType.</param>
        /// <returns>The formula, with all identifiers converted to invariant form.</returns>
        public string GetInvariantExpression(string expressionText, RecordType parameters)
        {
            return ExpressionLocalizationHelper.ConvertExpression(expressionText, parameters, BindingConfig.Default, CreateResolverInternal(), CreateBinderGlue(), Config, toDisplay: false);
        }

        /// <summary>
        /// Convert references in an expression to the display form.
        /// </summary>
        /// <param name="expressionText">textual representation of the formula.</param>
        /// <param name="parameters">Type of parameters for formula. The fields in the parameter record can 
        /// be acecssed as top-level identifiers in the formula. If DisplayNames are used, make sure to have that mapping
        /// as part of the RecordType.</param>
        /// <returns>The formula, with all identifiers converted to display form.</returns>
        public string GetDisplayExpression(string expressionText, RecordType parameters)
        {
            var symbols = SymbolTable.NewFromRecord(parameters);
            return GetDisplayExpression(expressionText, symbols);
        }

        public string GetDisplayExpression(string expressionText, ReadOnlySymbolTable symbolTable)
        {
            return ExpressionLocalizationHelper.ConvertExpression(expressionText, null, BindingConfig.Default, CreateResolverInternal(symbolTable), CreateBinderGlue(), Config, toDisplay: true);
        }
    }
}
