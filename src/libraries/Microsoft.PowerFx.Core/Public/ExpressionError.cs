﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.PowerFx.Core.Errors;
using Microsoft.PowerFx.Core.Localization;
using Microsoft.PowerFx.Syntax;
using Microsoft.PowerFx.Types;

namespace Microsoft.PowerFx
{
    /// <summary>
    /// Error message. This could be a compile time error from parsing or binding, 
    /// or it could be a runtime error wrapped in a <see cref="ErrorValue"/>.
    /// </summary>
    public class ExpressionError
    {
        /// <summary>
        /// A description of the error message. 
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// Source location for this error.
        /// </summary>
        public Span Span { get; set; }

        /// <summary>
        /// Runtime error code.This may be empty for compile-time errors. 
        /// </summary>
        public ErrorKind Kind { get; set; }

        public ErrorSeverity Severity { get; set; } = ErrorSeverity.Severe;

        public string MessageKey { get; set; }

        /// <summary>
        /// A warning does not prevent executing the error. See <see cref="Severity"/> for more details.
        /// </summary>
        public bool IsWarning => Severity < ErrorSeverity.Severe;

        public override string ToString()
        {
            var prefix = IsWarning ? "Warning" : "Error";
            if (Span != null)
            {
                return $"{prefix} {Span.Min}-{Span.Lim}: {Message}";
            }
            else
            {
                return $"{prefix} {Message}";
            }    
        }

        // Build the public object from an internal error object. 
        internal static ExpressionError New(IDocumentError error)
        {
            return new ExpressionError
            {
                Message = error.ShortMessage,
                Span = error.TextSpan,
                Severity = (ErrorSeverity)error.Severity,
                MessageKey = error.MessageKey
            };
        }

        internal static ExpressionError New(IDocumentError error, CultureInfo locale)
        {
            (var shortMessage, var _) = ErrorUtils.GetLocalizedErrorContent(new ErrorResourceKey(error.MessageKey), locale, out _);           

            return new ExpressionError
            {
                Message = ErrorUtils.FormatMessage(shortMessage, locale, error.MessageArgs),
                Span = error.TextSpan,
                Severity = (ErrorSeverity)error.Severity,
                MessageKey = error.MessageKey
            };
        }

        internal static IEnumerable<ExpressionError> New(IEnumerable<IDocumentError> errors)
        {
            if (errors == null)
            {
                return Array.Empty<ExpressionError>();
            }
            else
            {
                return errors.Select(x => ExpressionError.New(x)).ToArray();
            }
        }

        internal static IEnumerable<ExpressionError> New(IEnumerable<IDocumentError> errors, CultureInfo locale)
        {
            if (errors == null)
            {
                return Array.Empty<ExpressionError>();
            }
            else
            {
                return errors.Select(x => ExpressionError.New(x, locale)).ToArray();
            }
        }
    }
}
