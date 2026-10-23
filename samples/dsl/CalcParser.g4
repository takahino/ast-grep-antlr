parser grammar CalcParser;

options { tokenVocab = CalcLexer; }

program   : statement* EOF ;
statement : ID ASSIGN expr SEMI ;
expr      : expr STAR expr
          | expr SLASH expr
          | expr PLUS expr
          | expr MINUS expr
          | LPAREN expr RPAREN
          | NUMBER
          | ID
          ;
