using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Pol2RunUO.Converters
{
    internal static class MobileSourceGenerator
    {
        public static void Generate()
        {
            CompilationUnit()
                .WithUsings(GetUsingDirectives())
                .WithMembers(SingletonList<MemberDeclarationSyntax>(GetClassDeclaration()))
                ;
        }

        private static SyntaxList<UsingDirectiveSyntax> GetUsingDirectives() => List(
            new[]
            {
                UsingDirective(IdentifierName("System")),
                UsingDirective(IdentifierName("Server")),
                UsingDirective(QualifiedName(IdentifierName("Server"), IdentifierName("Misc"))),
                UsingDirective(QualifiedName(IdentifierName("Server"), IdentifierName("Items")))
            }
        );

        private static SyntaxNode GetClassNamespace() => NamespaceDeclaration(
            QualifiedName(IdentifierName("Server"), IdentifierName("Mobiles"))
        );


        private static ClassDeclarationSyntax GetClassDeclaration()
        {
            return ClassDeclaration("Wisp")
                    .WithAttributeLists(GetAttributes())
                    .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
                    .WithBaseList(BaseList(
                        SingletonSeparatedList<BaseTypeSyntax>(SimpleBaseType(IdentifierName("BaseCreature")))))
                ;
        }

        private static ConstructorInitializerSyntax GetConstructorInitializer()
        {
            var aiType = Argument(
                MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName("AIType"),
                    IdentifierName("AI_Mage")));

            var fightMode = Argument(
                MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName("FightMode"),
                    IdentifierName("Aggressor")));

            var perceptionRange = Argument(
                LiteralExpression(
                    SyntaxKind.NumericLiteralExpression,
                    Literal(10)));

            var activeSpeed = Argument(
                LiteralExpression(
                    SyntaxKind.NumericLiteralExpression,
                    Literal(1)));
            

            return ConstructorInitializer(
                SyntaxKind.BaseConstructorInitializer,
                ArgumentList(
                    SeparatedList<ArgumentSyntax>(
                        new SyntaxNodeOrToken[]
                        {
                            aiType,
                            Token(SyntaxKind.CommaToken),
                            fightMode,
                            Token(SyntaxKind.CommaToken),
                            perceptionRange,
                            Token(SyntaxKind.CommaToken),
                            activeSpeed,
                            Token(SyntaxKind.CommaToken),
                            Argument(
                                LiteralExpression(
                                    SyntaxKind.NumericLiteralExpression,
                                    Literal(0.2))),
                            Token(SyntaxKind.CommaToken),
                            Argument(
                                LiteralExpression(
                                    SyntaxKind.NumericLiteralExpression,
                                    Literal(0.4)))
                        })));
        }

        private static SyntaxList<AttributeListSyntax> GetAttributes()
        {
            return SingletonList(AttributeList(SingletonSeparatedList(CorpseAttribute("a wisp corpse"))));
        }


        private static AttributeSyntax ConstructableAttribute() => Attribute(IdentifierName("Constructable"));


        private static AttributeSyntax CorpseAttribute(string corpseName)
        {
            var arg = AttributeArgument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(corpseName)));
            return Attribute(IdentifierName("CorpseName"))
                .WithArgumentList(AttributeArgumentList(SingletonSeparatedList(arg)));
        }
    }
}