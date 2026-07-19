; extends

((comment
  content: (comment_content) @injection.content)
 (#contains? @injection.content "# viset")
 (#set! injection.language "toml"))

((function_call
  name: (dot_index_expression
    table: (identifier) @_viset
    field: (identifier) @_javascript)
  arguments: (arguments
    (string
      content: (string_content) @injection.content)))
 (#eq? @_viset "viset")
 (#eq? @_javascript "javascript")
 (#set! injection.language "javascript"))
