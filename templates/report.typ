#let metadata = json("../" + sys.inputs.at("metadata"))
#set document(
  title: metadata.title,
  author: metadata.author,
)
#set page(width: 13.833in, height: 8in, margin: 0pt, fill: white)

#let page-name(number) = if number < 10 { "0" + str(number) } else { str(number) }

#for page-number in range(1, metadata.pages + 1) {
  image(
    "../" + metadata.pages_dir + "/page-" + page-name(page-number) + ".svg",
    width: 100%,
    height: 100%,
    fit: "stretch",
  )
  if page-number < metadata.pages { pagebreak() }
}
