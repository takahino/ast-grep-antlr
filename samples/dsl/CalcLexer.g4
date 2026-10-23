lexer grammar CalcLexer;

PLUS  : '+';
MINUS : '-';
STAR  : '*';
SLASH : '/';
LPAREN: '(';
RPAREN: ')';
ASSIGN: '=';
SEMI  : ';';
NUMBER: [0-9]+ ('.' [0-9]+)?;
ID    : [a-zA-Z_][a-zA-Z0-9_]*;
WS    : [ \t\r\n]+ -> skip;
