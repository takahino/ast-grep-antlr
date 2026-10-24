import java.awt.BorderLayout;
import java.awt.Dimension;
import java.awt.GridBagConstraints;
import java.awt.GridBagLayout;
import java.awt.Insets;
import java.util.List;
import java.util.Map;
import javax.swing.JButton;
import javax.swing.JComboBox;
import javax.swing.JFrame;
import javax.swing.JLabel;
import javax.swing.JOptionPane;
import javax.swing.JPanel;
import javax.swing.JScrollPane;
import javax.swing.JTable;
import javax.swing.JTextField;
import javax.swing.SwingUtilities;
import javax.swing.table.DefaultTableModel;

public final class MainFrame extends JFrame {
  private final JComboBox<String> lang = new JComboBox<>(new String[] {"java", "csharp"});
  private final JTextField pattern = new JTextField("foo($$$ARGS)");
  private final JTextField path = new JTextField();
  private final DefaultTableModel model =
      new DefaultTableModel(new Object[] {"File", "Line", "Col", "Text"}, 0) {
        @Override
        public boolean isCellEditable(int row, int column) {
          return false;
        }
      };
  private final JLabel status = new JLabel(" ");

  public MainFrame() {
    super("ag4 sample GUI (Java / JNA)");
    setDefaultCloseOperation(JFrame.EXIT_ON_CLOSE);
    setPreferredSize(new Dimension(900, 560));

    try {
      path.setText(Ag4Native.defaultFixturesDir());
      status.setText("ag4_native " + Ag4Native.version() + "  lib=" + Ag4Native.resolveLibDir());
    } catch (Exception ex) {
      status.setText(ex.getMessage());
    }

    JPanel form = new JPanel(new GridBagLayout());
    GridBagConstraints c = new GridBagConstraints();
    c.insets = new Insets(4, 8, 4, 8);
    c.anchor = GridBagConstraints.WEST;
    c.fill = GridBagConstraints.HORIZONTAL;
    c.weightx = 0;
    c.gridx = 0;
    c.gridy = 0;
    form.add(new JLabel("Language"), c);
    c.gridx = 1;
    c.weightx = 1;
    form.add(lang, c);
    c.gridx = 0;
    c.gridy = 1;
    c.weightx = 0;
    form.add(new JLabel("Pattern"), c);
    c.gridx = 1;
    c.weightx = 1;
    form.add(pattern, c);
    c.gridx = 0;
    c.gridy = 2;
    c.weightx = 0;
    form.add(new JLabel("Path"), c);
    c.gridx = 1;
    c.weightx = 1;
    form.add(path, c);
    JButton search = new JButton("Search");
    c.gridx = 1;
    c.gridy = 3;
    c.weightx = 0;
    form.add(search, c);
    search.addActionListener(e -> runSearch());

    add(form, BorderLayout.NORTH);
    add(new JScrollPane(new JTable(model)), BorderLayout.CENTER);
    add(status, BorderLayout.SOUTH);
    pack();
    setLocationRelativeTo(null);
  }

  private void runSearch() {
    model.setRowCount(0);
    try {
      String req =
          SimpleJson.searchRequest(
              pattern.getText(),
              String.valueOf(lang.getSelectedItem()),
              path.getText(),
              200);
      Ag4Native.Result result = Ag4Native.search(req);
      if (result.code() != 0) {
        status.setText("Error (" + result.code() + "): " + SimpleJson.errorMessage(result.json()));
        return;
      }
      List<Map<String, Object>> rows = SimpleJson.asObjectArray(result.json());
      for (Map<String, Object> m : rows) {
        @SuppressWarnings("unchecked")
        Map<String, Object> range = (Map<String, Object>) m.get("range");
        @SuppressWarnings("unchecked")
        Map<String, Object> start = (Map<String, Object>) range.get("start");
        int line = ((Number) start.get("line")).intValue() + 1;
        int col = ((Number) start.get("column")).intValue() + 1;
        model.addRow(new Object[] {m.get("file"), line, col, m.get("text")});
      }
      status.setText(rows.size() + " match(es)");
    } catch (Exception ex) {
      status.setText(ex.getMessage());
      JOptionPane.showMessageDialog(this, ex.getMessage(), "ag4", JOptionPane.ERROR_MESSAGE);
    }
  }

  public static void main(String[] args) {
    if (args.length == 1 && "--smoke".equals(args[0])) {
      System.exit(smoke());
      return;
    }
    SwingUtilities.invokeLater(() -> new MainFrame().setVisible(true));
  }

  private static int smoke() {
    try {
      System.out.println("version=" + Ag4Native.version());
      String req =
          SimpleJson.searchRequest("foo($$$ARGS)", "java", Ag4Native.defaultFixturesDir(), 10);
      Ag4Native.Result r = Ag4Native.search(req);
      System.out.println("rc=" + r.code());
      System.out.println(r.json());
      if (r.code() != 0) {
        return 1;
      }
      int n = SimpleJson.asObjectArray(r.json()).size();
      System.out.println("matches=" + n);
      return n > 0 ? 0 : 2;
    } catch (Exception ex) {
      ex.printStackTrace();
      return 3;
    }
  }
}
